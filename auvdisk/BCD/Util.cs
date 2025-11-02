using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using auvdisk.Log;
using DiscUtils.BootConfig;
using DiscUtils.Registry;
using Spectre.Console;

namespace auvdisk.BCD
{
    public record BcdRecord
    {
        public static string RecordToString(BcdRecord record, bool verbose = false, bool markup = false)
        {
            var properties = record.GetType().GetProperties();
            var ignoreProperties = new string[] { "RawProperties", "HumanReadableName" };

            return new string[]
                {
                }
                .UnionIf($"Type: {record.GetType().Name}", () => !record.AuWellKnown || verbose)
                .Union(properties.Where(prop => !ignoreProperties.Contains(prop.Name)).Select(prop => $"{prop.Name.Markup(markup, "yellow")}: {prop.GetValue(record, null)?.Markup(markup)}"))
                .UnionIf("RawProperties:".Markup(markup, "yellow"), () => !record.AuWellKnown || verbose)
                .UnionIf(record.RawProperties.Select(prop => $"    {prop.Key.Markup(markup, "yellow")}: {prop.Value.Markup(markup)}"), () => !record.AuWellKnown || verbose)
                .Aggregate((x, y) => $"{x}{Environment.NewLine}{y}");
        }

        protected BcdRecord(BcdObject obj)
        {
            ApplicationType = obj.ApplicationType;
            ApplicationImageType = obj.ApplicationImageType;
            Identity = obj.Identity;
            RawProperties = obj.Elements
                .Select(x => new KeyValuePair<string, string>(x.FriendlyName, x?.Value?.ToString() ?? ""))
                .ToDictionary();
        }

        public override string ToString()
        {
            return RecordToString(this, false);
        }

        public ApplicationType ApplicationType { get; set; }
        public ApplicationImageType ApplicationImageType { get; set; }
        public Guid Identity { get; set; }
        public Dictionary<string, string> RawProperties { get; set; }
        public virtual bool AuWellKnown => false;
        public virtual string? HumanReadableName => null;

        public static BcdRecord Factory(BcdObject obj)
        {
            if (obj is { ApplicationImageType: ApplicationImageType.WindowsBoot, ApplicationType: ApplicationType.OsLoader })
            {
                return new WindowsOsLoaderBcdRecord(obj);
            }
            else
            {
                return new BcdRecord(obj);
            }
        }
    }

    public record WindowsOsLoaderBcdRecord : BcdRecord
    {
        private string? CrudeExtractFilePathFromBinaryDeviceEntry(BcdObject obj, string name)
        {
            Element? el = obj.Elements.FirstOrDefault(x => x.FriendlyName == name);

            if (el != null)
            {
                FieldInfo[] fields = typeof(Element).GetFields(
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

                var storage = fields.First(x => x.Name == "_storage").GetValue(el)!;
                int identifier = (int)fields.First(x => x.Name == "_identifier").GetValue(el)!;
                Guid guid = (Guid)fields.First(x => x.Name == "_obj").GetValue(el)!;

                byte[] result = (byte[])storage.GetType().GetMethod("GetBinary")!.Invoke(storage, [guid, identifier])!;

                var resultString = Encoding.Unicode.GetString(result);
                var regex = new Regex(@"\\[\\\w\d-_\.]+\0$");
                var match = regex.Match(resultString);

                return regex.IsMatch(resultString) ? "?" + match.Value : null;
            }

            return null;
        }

        public WindowsOsLoaderBcdRecord(BcdObject obj) : base(obj)
        {
            Description = obj.FindElement("{description}") ?? "";
            KernelPath = obj.FindElement("{path}") ?? "";
            Device = CrudeExtractFilePathFromBinaryDeviceEntry(obj, "{device}") ?? obj.FindElement("{device}") ?? "";
            OsDevice = CrudeExtractFilePathFromBinaryDeviceEntry(obj, "{osdevice}") ?? obj.FindElement("{osdevice}") ?? "";
            Locale = obj.FindElement("{locale}") ?? "";
            SystemRoot = obj.FindElement("{systemroot}") ?? "";
        }

        public string Description { get; set; }
        public string KernelPath { get; set; }
        public string Device { get; set; }
        public string OsDevice { get; set; }
        public string Locale { get; set; }
        public string SystemRoot { get; set; }
        public override bool AuWellKnown => true;
        public override string? HumanReadableName => Description;

        public override string ToString()
        {
            return RecordToString(this, false);
        }
    }

    public static class Util
    {
        public static void ProbeBcd(string path, bool verbose, ILog logger)
        {
            using var fileStream = File.OpenRead(path);
            using var hive = new RegistryHive(fileStream);

            var bcdDb = new Store(hive.Root);
            var records = bcdDb.Objects.Select(x => BcdRecord.Factory(x)!).Where(x => x.AuWellKnown || verbose).ToList();

            foreach (var record in records)
            {
                var humanReadableName = record.HumanReadableName ?? "BCD Record";

                logger.Log(new Rule($"[green]{humanReadableName.EscapeMarkup()}[/]").LeftJustified());
                logger.Log(BcdRecord.RecordToString(record, verbose, true));
            }
        }

        public static IEnumerable<TSource> Union<TSource>(this IEnumerable<TSource> first, TSource element)
        {
            return first.Union(new List<TSource> {element});
        }

        public static IEnumerable<TSource> UnionIf<TSource>(this IEnumerable<TSource> first, TSource element,
            Func<bool> predicate)
        {
            if (predicate())
            {
                return first.Union(new List<TSource> {element});
            }
            else
            {
                return first;
            }
        }

        public static IEnumerable<TSource> UnionIf<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second,
            Func<bool> predicate)
        {
            if (predicate())
            {
                return first.Union(second);
            }
            else
            {
                return first;
            }
        }

        public static string? FindElement(this BcdObject obj, string elementName)
        {
            return obj.Elements.FirstOrDefault(el => el.FriendlyName == elementName)?.Value?.ToString();
        }

        public static T? FindElement<T>(this BcdObject obj, string elementName) where T : class
        {
            return obj.Elements.FirstOrDefault(el => el.FriendlyName == elementName)?.Value as T;
        }

        public static string Markup(this object obj, bool markup, string? color = null)
        {
            if (markup)
            {
                if (color == null)
                {
                    return obj.ToString().EscapeMarkup();
                }
                else
                {
                    return $"[{color}]{obj.ToString().EscapeMarkup()}[/]";
                }
            }
            else
            {
                return obj.ToString() ?? "";
            }
        }
    }
}