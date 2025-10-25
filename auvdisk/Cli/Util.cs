using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace auvdisk.Cli
{
    public static class Util
    {
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        public static List<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
        {
            var foundTypes = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();

                    var typesWithAttribute = types
                        .Where(type => type.IsDefined(typeof(TAttribute), inherit: false));

                    foundTypes.AddRange(typesWithAttribute);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Optionally handle assemblies that can't be loaded fully (e.g., skip them)
                    continue;
                }
            }

            return foundTypes;
        }
    }
}