using System.Reflection;
using CommandLine;
using Spectre.Console;
using auvdisk.Extensions;
using auvdisk.Log;

namespace auvdisk.Cli;

public static class HelpRenderer
{
    internal static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errors, ILog logger)
    {
        var isHelpRequest = errors.Any(e => e is HelpRequestedError || e is HelpVerbRequestedError);
        var isVersionRequest = errors.Any(e => e is VersionRequestedError);
        var isSpecificVerb = result.TypeInfo.Current != typeof(NullInstance);
        var myName = Assembly.GetEntryAssembly()!.GetName().Name;
        var myVersion = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+').FirstOrDefault() ?? "?";
        var myLongVersion = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

        var formatter = FormatError;

        var errorStrings = errors.Select(error => formatter(error)).Where(error => error != "").ToList();

        if (errorStrings.Any())
        {
            errorStrings.Add("");
        }

        foreach (var errorString in errorStrings)
        {
            Utils.IfElse(() => errorString == "", AnsiConsole.WriteLine, () => logger.Error(errorString));
        }
        
        if (isVersionRequest)
        {
            AnsiConsole.MarkupLine($"[bold]{myName}[/] [blue]{myLongVersion}[/]");
            return;
        }
        
        if (isSpecificVerb)
        {
            DisplayVerbSpecificHelp(result.TypeInfo.Current);
            return;
        }
        
        var rule = new Rule($"[bold yellow]{myName} - virtual disk image manipulation tool[/]");
        rule.Justification = Justify.Left;
        
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Version:[/] v{myVersion}");
        AnsiConsole.MarkupLine($"[grey]Usage:[/] {myName} [[verb]] [[options]]");
        AnsiConsole.WriteLine();
        
        var verbsTable = new Table();
        verbsTable.Border(TableBorder.Rounded);
        verbsTable.AddColumn(new TableColumn("[bold]Verb[/]").LeftAligned());
        verbsTable.AddColumn(new TableColumn("[bold]Description[/]"));

        var verbTypes = VerbHandlers.GetVerbTypes(false, false);

        foreach (var verbType in verbTypes)
        {
            var verbAttr = verbType.GetCustomAttributes(typeof(VerbAttribute), false)
                .OfType<VerbAttribute>()
                .FirstOrDefault();

            if (verbAttr != null)
            {
                verbsTable.AddRow(
                    $"[cyan]{verbAttr.Name}[/]",
                    verbAttr.HelpText ?? ""
                );
            }
        }

        verbsTable.AddRow($"[cyan]help[/]", "This help screen");
        verbsTable.AddRow($"[cyan]version[/]", "Display version information");

        AnsiConsole.Write(verbsTable);
        AnsiConsole.MarkupLine($"[dim]Use '{myName} [[verb]] --help' for more information about a specific verb.[/]");
        AnsiConsole.WriteLine();
    }

    private static void DisplayVerbSpecificHelp(Type verbType)
    {
        var verbAttr = verbType.GetCustomAttributes(typeof(VerbAttribute), false)
            .OfType<VerbAttribute>()
            .FirstOrDefault();

        if (verbAttr == null) return;

        AnsiConsole.MarkupLine($"[bold yellow]{verbAttr.Name}[/] - {verbAttr.HelpText}");
        AnsiConsole.WriteLine();
        
        var optionsTable = new Table();
        optionsTable.Border(TableBorder.Rounded);
        optionsTable.AddColumn(new TableColumn("[bold]Option[/]"));
        optionsTable.AddColumn(new TableColumn("[bold]Required[/]").Centered());
        optionsTable.AddColumn(new TableColumn("[bold]Default[/]"));
        optionsTable.AddColumn(new TableColumn("[bold]Type[/]"));
        optionsTable.AddColumn(new TableColumn("[bold]Description[/]"));

        var properties = verbType.GetProperties();

        string DefaultToString(object? subj)
        {
            return subj switch
            {
                null => "[dim]<null>[/]",
                string { Length: 0 } => "[dim]<empty>[/]",
                bool subjBool => subjBool ? "true" : "false",
                _ => subj.ToString() ?? "[dim]?[/]"
            };
        }

        string BeautifyType(string type)
        {
            return type switch
            {
                "Boolean" => "bool",
                "String" => "string",
                "Double" => "double",
                "Single" => "float",
                "Decimal" => "decimal",
                _ when type.StartsWith("int", StringComparison.InvariantCultureIgnoreCase) => "int",
                _ when type.StartsWith("uint", StringComparison.InvariantCultureIgnoreCase) => "uint",
                _ => type
            };
        }

        foreach (var prop in properties)
        {
            var optionAttr = prop.GetCustomAttributes(typeof(OptionAttribute), false)
                .OfType<OptionAttribute>()
                .FirstOrDefault();

            if (optionAttr == null) continue;
            
            var shortName = optionAttr.ShortName != "" ? $"-{optionAttr.ShortName}" : "";
            var longName = !string.IsNullOrEmpty(optionAttr.LongName) ? $"--{optionAttr.LongName}" : "";
            var optionName = string.Join(", ", new[] { shortName, longName }.Where(s => !string.IsNullOrEmpty(s)));

            var required = optionAttr.Required ? "[green]Yes[/]" : "[dim]No[/]";
            var defaultValue = DefaultToString(optionAttr.Default);
            var description = optionAttr.HelpText ?? "";

            optionsTable.AddRow(
                $"[cyan]{optionName}[/]",
                required,
                defaultValue ?? "",
                BeautifyType(prop.PropertyType.Name),
                description
            );
        }

        AnsiConsole.Write(optionsTable);
    }
    
    private static string FormatError(Error error)
    {
        var formatter = CommandLine.Text.SentenceBuilder.Factory().FormatError;

        try
        {
            return formatter(error);
        }
        catch (Exception)
        {
            return "";
        }
    }
}