using System.ComponentModel.DataAnnotations;
using System.Reflection;
using auvdisk.Extensions;
using auvdisk.Log;
using Spectre.Console;

namespace auvdisk.Cli;

public static class TableRenderer
{
    public static class EnumerableHelper
    {
        private static bool IsPlainEnumerable(object? obj, bool excludeStrings = true)
        {
            if (obj == null) return false;
            if (excludeStrings && obj is string) return false;
            return obj is System.Collections.IEnumerable;
        }
    
        private static bool IsGenericEnumerable(object? obj, bool excludeStrings = true)
        {
            if (obj == null) return false;
            if (excludeStrings && obj is string) return false;
            
            var type = obj.GetType();
            return type.GetInterfaces()
                .Any(i => i.IsGenericType && 
                          i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }

        public static bool IsEnumerable(object? obj, bool excludeStrings = true)
        {
            return IsPlainEnumerable(obj, excludeStrings) || IsGenericEnumerable(obj, excludeStrings);
        }
        
        public static Type? GetEnumerableElementType(object obj)
        {
            var type = obj.GetType();
            var enumInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && 
                                     i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        
            return enumInterface?.GetGenericArguments().First() ?? null;
        }
    }
    
    public static class ToStringHelper
    {
        private static readonly HashSet<Type> MeaningfulToStringTypes =
        [
            typeof(string),
            typeof(char),
            typeof(bool),
            typeof(byte), typeof(sbyte),
            typeof(short), typeof(ushort),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
            typeof(float), typeof(double),
            typeof(decimal),

            // Common value types
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(Guid),
            typeof(Uri)

            // Nullable versions are handled separately
        ];

        public static bool HasMeaningfulToString(Type type)
        {
            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        
            // Check if it's in our known list
            if (MeaningfulToStringTypes.Contains(underlyingType))
                return true;
        
            // Check if it's an enum
            if (underlyingType.IsEnum)
                return true;
        
            // Check if ToString is overridden (not just inherited from Object)
            var toStringMethod = underlyingType.GetMethod("ToString", Type.EmptyTypes);
            if (toStringMethod != null && toStringMethod.DeclaringType != typeof(object))
                return true;
        
            return false;
        }
    }

    public static string ToStrImpl(object? subj)
    {
        if (subj == null)
        {
            return "<null>";
        }
        else if (EnumerableHelper.IsEnumerable(subj))
        {
            if (subj is System.Collections.IEnumerable collection)
            {
                List<object> subjForLinq = new List<object>();
                        
                foreach (var el in collection)
                {
                    subjForLinq.Add(el);
                }
                
                return string.Join(", ", subjForLinq.Select(x => x.ToString()));
            }
            else
            {
                return subj.GetType().Name;
            }
        }
        else if (ToStringHelper.HasMeaningfulToString(subj.GetType()))
        {
            return subj.ToString() ?? "";
        }
        else
        {
            return subj.GetType().Name;
        }
    }

    private static void RenderClassWithProps<T>(List<PropertyInfo> props, IEnumerable<T> subj, ILog logger)
    {
        var table = Utils.MakeConsoleTable(props.Select(x => x.GetCustomAttribute<DisplayAttribute>()?.Name ?? x.Name).ToArray());

        foreach (var el in subj)
        {
            table.AddRow(props.Select(p => ToStrImpl(p.GetValue(el))).ToArray());
        }

        logger.Log(table);
    }

    private static void RenderRecordOneliner<T>(Type type, ConstructorInfo ctor, IEnumerable<T> subj, ILog logger)
    {
        var table = Utils.MakeConsoleTable(ctor.GetParameters().Select(x => x.Name ?? x.Position.ToString()).ToArray());

        foreach (var el in subj)
        {
            table.AddRow(ctor.GetParameters().Where(x => x.Name != null).Select(param => ToStrImpl(type.GetProperty(param.Name!)?.GetValue(el))).ToArray());
        }
                
        logger.Log(table);
    }

    public static void RenderTable<T>(IEnumerable<T> subjRaw, ILog logger, bool showAll = false)
    {
        var subj = subjRaw.ToList();

        if (!subj.Any()) return;
        
        var type = subj.First()!.GetType();
        var props = type.GetProperties().Where(x => x.GetCustomAttribute<DisplayAttribute>() != null || showAll).ToList();
            
        if (props.Any()) // class with properties
        {
            RenderClassWithProps(props, subj, logger);
        }
        else if (type.GetConstructors().FirstOrDefault() is { } ctor) // one-liner record definitions
        {
            RenderRecordOneliner(type, ctor, subj, logger);
        }
    }
}