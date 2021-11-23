using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using RimDev.AspNetCore.FeatureFlags;
using System.Threading.Tasks;

namespace FeatureFlags.AspNetCore
{

    public class ConfigurationAppsettingKeyNameConstants
    {
        public const string ProjectionStoreBaseUrl = "ProjectionStore:BaseUrl";
        public const string ProjectionStoreUserName = "ProjectionStore:UserName";
        public const string ProjectionStoreUserPassword = "ProjectionStore:UserPassword";
        public const string ProjectionStoreClusterName = "ProjectionStore:ClusterName";
        public const string ProjectionStoreDatabaseName = "ProjectionStore:DatabaseName";
        public const string ProjectionStoreWriteRetries = "ProjectionStore:WriteRetries";
        public const string ProjectionStoreWriteConcern = "ProjectionStore:WriteConcern";
        public const string ProjectionStoreCollectionName = "ProjectionStore:CollectionName";
    }

public class Startup
    {
        private  FeatureFlagOptions options;

        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            //options = new FeatureFlagOptions()
            //    .UseInMemoryFeatureProvider();
                // .UseCachedSqlFeatureProvider(Configuration.GetConnectionString("localDb"));
        }

        public void ConfigureServices(IServiceCollection services)
        {
            ConfigureInspectionServices(services);
            services.AddFeatureFlags(options);
        }

        private void ConfigureInspectionServices(IServiceCollection services)
        {

            var projectionStoreBaseUrl = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreBaseUrl];
            var projectionStoreUserName = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreUserName];
            var projectionStoreUserPassword = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreUserPassword];
            var projectionStoreClusterName = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreClusterName];
            var projectionStoreDatabaseName = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreDatabaseName];
            var projectionStoreWriteRetries = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreWriteRetries];
            var projectionStoreWriteConcern = Configuration[ConfigurationAppsettingKeyNameConstants.ProjectionStoreWriteConcern];
            var projectionStoreConnectionString = $"{projectionStoreBaseUrl}{projectionStoreUserName}:{projectionStoreUserPassword}@{projectionStoreClusterName}/{projectionStoreDatabaseName}?retryWrites={projectionStoreWriteRetries}&w={projectionStoreWriteConcern}";

            options = new FeatureFlagOptions()
               .UseMongoDBFeatureProvider(new MongoClient(projectionStoreConnectionString));
        }
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseFeatureFlags(options);
            //app.UseFeatureFlagsUI(options);

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.Map("/test-features", async context =>
                {
                    var testFeature = context.RequestServices.GetService<TestFeature>();
                    var testFeature2 = context.RequestServices.GetService<TestFeature2>();
                    var testFeature3 = context.RequestServices.GetService<TestFeature3>();

                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync($@"
                    {testFeature.GetType().Name}: {testFeature.Value}<br />
                    {testFeature2.GetType().Name}: {testFeature2.Value}<br />
                    {testFeature3.GetType().Name}: {testFeature3.Value}<br />
                    <a href=""{options.UiPath}"">View UI</a>");
                });

                endpoints.Map("", context =>
                {
                    context.Response.Redirect("/test-features");

                    return Task.CompletedTask;
                });

                endpoints.MapFeatureFlagsUI(options);
            });
        }
    }
}
