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
        Action<string> ToAction();
    }

    public class Logger : ILog
    {
        public void Log(IRenderable log)
        {
            AnsiConsole.Write(log);
        }

        public void Error(string error)
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {error}[/]");
        }

        public void Warning(string warning)
        {
            AnsiConsole.MarkupLine($"[yellow]WARNING: {warning}[/]");
        }

        public void Log(string s)
        {
            if (s.StartsWith("ERROR"))
            {
                AnsiConsole.MarkupLine($"[red]{s}[/]");
            }
            else if (s.StartsWith("WARNING"))
            {
                AnsiConsole.MarkupLine($"[yellow]{s}[/]");
            }
            else
            {
                try
                {
                    AnsiConsole.MarkupLine(s);
                }
                catch (InvalidOperationException)
                {
                    AnsiConsole.WriteLine(s);
                }
            }
        }

        public Action<string> ToAction()
        {
            return Log;
        }
    }

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

        public Action<string> ToAction()
        {
            return Log;
        }
    }
}