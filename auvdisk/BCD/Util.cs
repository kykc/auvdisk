using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using auvdisk.Log;
using DiscUtils.BootConfig;
using DiscUtils.Registry;
using DotNext.Collections.Generic;
using Spectre.Console;

namespace auvdisk.BCD
{
    public record BcdRecord
    {
        public static string RecordToString(BcdRecord record, bool verbose = false, bool markup = false)
        {
            var properties = record.GetType().GetProperties();
            var ignoreProperties = new string[] { "RawProperties", "HumanReadableName" };

            return new string[]{}
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
            return RecordToString(this, false, false);
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
        // Here's the deal: I failed to find reference manual and/or exhausting implementation
        // of the device pointer entry in BCD. DU also "underparses" it. So, because,
        // in my particular case I care about paths to VHD(x)s the most, I devised a
        // crude and dirty way to do it:
        // 1. Via reflection, I get private raw byte[] array of the binary device pointer entry
        // 2. Then (as if reflection was not enough) I use regex to extract all
        // null-terminated UTF-16 strings that start with \ from raw binary blob.
        //
        // I might burn in programmer's hell for it, but it does what I need surprisingly well.
        private static string? CrudeExtractFilePathsFromBinaryDeviceEntry(BcdObject obj, string name)
        {
            Element? el = obj.Elements.FirstOrDefault(x => x.FriendlyName == name);

            if (el != null)
            {
                var fields = typeof(Element).GetFields(
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

                var storage = fields.First(x => x.Name == "_storage").GetValue(el)!;
                int identifier = (int)fields.First(x => x.Name == "_identifier").GetValue(el)!;
                Guid guid = (Guid)fields.First(x => x.Name == "_obj").GetValue(el)!;

                byte[] result = (byte[])storage.GetType().GetMethod("GetBinary")!.Invoke(storage, [guid, identifier])!;

                var resultString = Encoding.Unicode.GetString(result);
                var regex = new Regex(@"\\[\\\w\d-_\.]+\0$");
                var matches = new Regex(@"\\[\\\w\d-_\.]+\0").Matches(resultString);

                if (matches.Count > 1)
                {
                    return (el?.Value?.ToString() ?? "") + "" + matches.Select(x => x.Value.TrimEnd('\0')).Aggregate((x, y) => x + "\\?" + y);
                }
                else if (regex.IsMatch(resultString))
                {
                    var match = regex.Match(resultString);

                    return (el?.Value?.ToString() ?? "") + "" + match.Value.TrimEnd('\0');
                }
            }

            return null;
        }

        public WindowsOsLoaderBcdRecord(BcdObject obj) : base(obj)
        {
            Description = obj.FindElement("{description}") ?? "";
            KernelPath = obj.FindElement("{path}") ?? "";
            Device = CrudeExtractFilePathsFromBinaryDeviceEntry(obj, "{device}") ?? obj.FindElement("{device}") ?? "";
            OsDevice = CrudeExtractFilePathsFromBinaryDeviceEntry(obj, "{osdevice}") ?? obj.FindElement("{osdevice}") ?? "";
            Locale = obj.FindElement("{locale}") ?? "";
            SystemRoot = obj.FindElement("{systemroot}") ?? "";
        }

        public string Description { get; set; }
        public string KernelPath { get; set; }
        public string Device { get; set; }
        public string OsDevice { get; set; }
        public string Locale { get; set; }
        public string SystemRoot { get; set; }
        public override bool AuWellKnown => RawProperties.GetOrInvoke("{recoveryos}", () => "").ToLower() != "true";
        public override string? HumanReadableName => Description;

        public override string ToString()
        {
            return RecordToString(this, false, false);
        }
    }

    public static class Util
    {
        public static IEnumerable<BcdRecord> ProbeBcd(string path, bool verbose, ILog logger)
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

            return records;
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