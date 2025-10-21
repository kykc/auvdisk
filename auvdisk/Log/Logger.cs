using Spectre.Console;
using Spectre.Console.Rendering;

namespace auvdisk.Log
{

    public interface ILog
    {
        void Log(IRenderable log);
        void Log(string log);
        void Error(string error);
    }

    public class Logger : ILog
    {
        public void Log(IRenderable log)
        {
            AnsiConsole.Write(log);
        }

        public void Error(string error)
        {
            AnsiConsole.Markup($"[red]ERROR: {error}[/]");
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
    }
}