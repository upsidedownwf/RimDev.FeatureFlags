using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RimDev.AspNetCore.FeatureFlags
{
    public static class IEnumerableExtensions
    {
        public static IEnumerable<Type> GetFeatureTypes(
            this IEnumerable<Assembly> featureFlagAssemblies)
        {
            return featureFlagAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(Feature)));
        }

        public static Type GetFeatureType(
            this IEnumerable<Assembly> featureFlagAssemblies,
            string featureName)
        {
            return GetFeatureTypes(featureFlagAssemblies)
                .SingleOrDefault(x => x.Name == featureName);
        }
        public static Type GetFeatureType(
            this Assembly featureFlagAssembly,
            string featureName)
        {
            var x= featureFlagAssembly.GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(Feature)));
            return featureFlagAssembly.GetTypes()
                .SingleOrDefault(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(Feature)) && type.Name == featureName);
        }
    }
}
