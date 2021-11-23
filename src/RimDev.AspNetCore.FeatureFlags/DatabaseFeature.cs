using MongoDB.Bson.Serialization.Attributes;

namespace RimDev.AspNetCore.FeatureFlags
{
    public class DatabaseFeature
    {
        [BsonElement("name")]
        public string Name { get; set; }

        [BsonElement("description")]
        public string Description { get; set; }

        [BsonElement("serviceLifetime")]
        public string ServiceLifetime { get; set; }

        [BsonElement("value")]
        public bool Value { get; set; }
    }
}
