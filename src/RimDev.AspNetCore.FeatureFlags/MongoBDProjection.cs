using MongoDB.Bson.Serialization.Attributes;

namespace RimDev.AspNetCore.FeatureFlags
{
    public class MongoBDProjection
    {
        [BsonElement("featureName")]
        public string FeatureName { get; set; }
        [BsonElement("description")]
        public string Description { get; set; }
        [BsonElement("value")]
        public bool Value { get; set; }
        [BsonElement("projectionType")]
        public string ProjectionType { get; set; }
    }
}
