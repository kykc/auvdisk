using System.Reflection;
using System.Text;

namespace auvdisk.Text
{
    public static class MarkdownGenerator
    {
        public static string? ToMarkdownTable<T>(IEnumerable<T> data, Log.ILog logger)
        {
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToArray();

            if (!properties.Any())
            {
                logger.Error("No readable properties found to generate a table.");
                return null;
            }

            var sb = new StringBuilder();

            List<int> lengths = Enumerable.Repeat(0, properties.Length).ToList();

            foreach (var (prop, idx) in properties.Select((p, i) => (p, i)))
            {
                foreach (var item in data)
                {
                    var value = prop.GetValue(item)?.ToString() ?? string.Empty;

                    lengths[idx] = Math.Max(lengths[idx], value.Length);
                }
            }

            var header = "| " + string.Join(" | ", properties.Select((p, idx) => p.Name.PadRight(lengths[idx]))) + " |";
            sb.AppendLine(header);

            var separator = "|-" + string.Join("|", properties.Select((_, idx) => string.Concat(Enumerable.Repeat('-', lengths[idx] + 1)))) + "-|";
            sb.AppendLine(separator);

            foreach (var item in data)
            {
                var values = properties.Select((p, idx) =>
                {
                    var value = p.GetValue(item);
                    return (value?.ToString() ?? string.Empty).PadRight(lengths[idx]);
                });

                var row = "| " + string.Join(" | ", values) + " |";
                sb.AppendLine(row);
            }

            return sb.ToString();
        }
    }
}