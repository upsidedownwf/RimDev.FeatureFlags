using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RimDev.AspNetCore.FeatureFlags
{
    public class MongoDBFeatureProvider : IFeatureProvider, IUseTransaction
    {
        IMongoCollection<MongoBDProjection> projectionStoreCollection;
        private readonly IMongoClient mongoClient;
        protected IClientSessionHandle session;
        private bool disposed;
        protected bool usingTransaction;
        protected IMongoDatabase database;
        private static readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All

        };
        private static bool databaseInitialized;

        private readonly ConcurrentDictionary<string, object> cache =
            new ConcurrentDictionary<string, object>();

        private static readonly SemaphoreSlim initializeDatabaseSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim hydrateCacheIfNeededSemaphore = new SemaphoreSlim(1, 1);

        private readonly IEnumerable<Assembly> featureFlagAssemblies;
        private readonly string databaseName;
        private readonly string collectionName;
        private readonly TimeSpan cacheLifetime;
        private DateTime? cacheLastUpdatedAt;
        private Assembly executingAssembly;

        public MongoDBFeatureProvider(
            IEnumerable<Assembly> featureFlagAssemblies,
            IMongoClient mongoClient,
            string databaseName = "FeatureToggler",
            string collectionName = "RimDevAspNetCoreFeatureFlags",
            TimeSpan? cacheLifetime = null)
        {
            this.featureFlagAssemblies = featureFlagAssemblies ?? throw new ArgumentNullException(nameof(featureFlagAssemblies));
            this.mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
            this.databaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
            this.collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            this.cacheLifetime = cacheLifetime ?? TimeSpan.FromMinutes(1);
            var database = mongoClient.GetDatabase(databaseName);
            projectionStoreCollection = mongoClient.GetDatabase(databaseName).GetCollection<MongoBDProjection>(collectionName);
            executingAssembly = featureFlagAssemblies.First();
        }
        public async Task Initialize()
        {
            if (!databaseInitialized)
            {
                await InitializeDatabase().ConfigureAwait(false);
            }

            await HydrateCacheIfNeeded().ConfigureAwait(false);
        }

        private async Task InitializeDatabase()
        {
            await initializeDatabaseSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                projectionStoreCollection = mongoClient.GetDatabase(databaseName).GetCollection<MongoBDProjection>(collectionName);

                databaseInitialized = true;
            }
            finally
            {
                initializeDatabaseSemaphore.Release();
            }
        }
        private async Task HydrateCacheIfNeeded()
        {
            Func<bool> cacheStale = () => !cacheLastUpdatedAt.HasValue
                || cacheLastUpdatedAt < DateTime.UtcNow.Subtract(cacheLifetime);

            if (cacheStale())
            {
                // Hydrate with all defined types
                foreach (var featureType in featureFlagAssemblies.GetFeatureTypes())
                {
                    var feature = Activator.CreateInstance(featureType);

                    var featureName = feature.GetType().Name;

                    cache.AddOrUpdate(featureName, feature, (_, __) => feature);
                }

                try
                {
                    await hydrateCacheIfNeededSemaphore.WaitAsync().ConfigureAwait(false);

                    // Another thread could have refreshed the cache since the previous statement
                    if (!cacheStale())
                    {
                        return;
                    }

                    // Overwrite cached items based on existing database items
                    var features = await GetAllFeaturesFromDatabase().ConfigureAwait(false);

                    // Filter out features that no longer exist but were persisted in the database previously
                    // to avoid JsonSerializationException on `JsonConvert.DeserializeObject` for missing type.
                    features = features
                        .Where(x => cache.TryGetValue(x.Key, out var cachedFeature) && cachedFeature != default)
                        .ToDictionary(x => x.Key, x => x.Value);

                    foreach (var feature in features)
                    {
                        var featureType = executingAssembly.GetFeatureType(feature.Key);
                        var value = (Feature)Activator.CreateInstance(featureType);
                        value.Value = feature.Value;

                        cache.AddOrUpdate(feature.Key, value, (_, __) => value);
                    }

                    cacheLastUpdatedAt = DateTime.UtcNow;
                }
                finally
                {
                    hydrateCacheIfNeededSemaphore.Release();
                }
            }
        }

        public async Task<Feature> Get(string featureName)
        {
            await HydrateCacheIfNeeded().ConfigureAwait(false);

            var valueExists = cache.TryGetValue(featureName, out object cacheValue);

            if (!valueExists)
                throw new ArgumentException($"{featureName} does not exist.");

            return (Feature)cacheValue;
        }
        private async Task<Dictionary<string, bool>> GetAllFeaturesFromDatabase()
        {
            var data = new Dictionary<string, bool>();
            var filter = Builders<MongoBDProjection>.Filter.Where(x => x.ProjectionType == nameof(MongoBDProjection));
            var findOptions = new FindOptions<MongoBDProjection>
            {
                Projection = "{_id: 0}",
                Sort = new SortDefinitionBuilder<MongoBDProjection>().Ascending("featureName"),
            };
            var cursor = await projectionStoreCollection.FindAsync(filter, findOptions);
            await cursor.ForEachAsync(projection => data.Add(projection.FeatureName, projection.Value));

            return data;
        }
        public async Task Set<TFeature>(TFeature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));

            var featureName = feature.GetType().Name;

            cache.AddOrUpdate(featureName, feature, (_, __) => feature);

            var serializedFeature = JsonConvert.SerializeObject(feature, jsonSerializerSettings);

            await SetFeatureInDatabase(featureName, serializedFeature).ConfigureAwait(false);
        }
        private async Task SetFeatureInDatabase(string featureName, string serializedFeature)
        {
            var feature = JsonConvert.DeserializeObject<DatabaseFeature>(serializedFeature);
            var projection = new MongoBDProjection
            {
                FeatureName = featureName,
                Description = feature.Description,
                Value = feature.Value,
                ProjectionType = nameof(MongoBDProjection)
            };

            var filter = Builders<MongoBDProjection>.Filter.Where(x => x.ProjectionType == nameof(MongoBDProjection) && x.FeatureName == featureName);
            await projectionStoreCollection.ReplaceOneAsync(filter, projection, new ReplaceOptions { IsUpsert = true });
        }
        public async Task StartTransaction()
        {
            if (!usingTransaction)
            {
                session = await mongoClient.StartSessionAsync();
                session.StartTransaction();
                usingTransaction = true;
            }
        }
        public async Task CommitTransaction()
        {
            await session.CommitTransactionAsync();
            session.Dispose();
            usingTransaction = false;
        }
        public async Task AbortTransaction()
        {
            await session.AbortTransactionAsync();
            session.Dispose();
            usingTransaction = false;
        }
        public void Dispose()
        {
            Dispose(disposing: true);
        }
        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (session != null)
                    {
                        session.Dispose();
                    }
                }
                disposed = true;
            }
        }
    }
}
