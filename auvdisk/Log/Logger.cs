using Common.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace auvdisk.Log
{

    public interface ILog
    {
        void Log(IRenderable log);
        void Log(string log);
        void Error(string error);
        void Warning(string warning);
        void Debug(string debug);
        Action<string> ToAction();
        Stream ToStream();

        LogLevel LogLevel { get; }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class Logger : ILog
    {
        public void Log(IRenderable log)
        {
            AnsiConsole.Write(log);
        }

        public void Error(string error)
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {error.EscapeMarkup()}[/]");
        }

        public void Warning(string warning)
        {
            AnsiConsole.MarkupLine($"[yellow]WARNING: {warning.EscapeMarkup()}[/]");
        }

        public void Debug(string debug)
        {
            if (LogLevel <= LogLevel.Debug)
            {
                AnsiConsole.MarkupLine(debug);
            }
        }

        public void Log(string s)
        {
            if (s.StartsWith("ERROR"))
            {
                AnsiConsole.MarkupLine($"[red]{s.EscapeMarkup()}[/]");
            }
            else if (s.StartsWith("WARNING"))
            {
                AnsiConsole.MarkupLine($"[yellow]{s.EscapeMarkup()}[/]");
            }
            else
            {
                try
                {
                    AnsiConsole.MarkupLine(s);
                }
                catch (InvalidOperationException)
                {
                    Warning("Failed to parse CLI markup for the following message, ignoring markup");
                    AnsiConsole.WriteLine(s);
                }
            }
        }

        public Action<string> ToAction()
        {
            return Log;
        }

        public Stream ToStream()
        {
            return Console.OpenStandardOutput();
        }

        public LogLevel LogLevel { get; set; } = Program.LogLevel;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class NullLogger : ILog
    {
        public void Log(IRenderable log)
        {
        }

        public void Log(string log)
        {
        }

        public void Error(string error)
        {
        }

        public void Warning(string warning)
        {
        }

        public void Debug(string debug)
        {
        }

        public Action<string> ToAction()
        {
            return Log;
        }

        public Stream ToStream()
        {
            return Stream.Null;
        }

        public LogLevel LogLevel => LogLevel.All;
    }
}