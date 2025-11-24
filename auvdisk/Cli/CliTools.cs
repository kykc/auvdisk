using auvdisk.Extensions;
using auvdisk.Log;
using SimpleExec;

namespace auvdisk.Cli
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class CliTools
    {
        public static Flow<Value<int>> AllocateWithDd(string filename, ulong size, ILog logger)
        {
            try
            {
                var (stdOut, stdErr) = SimpleExec.Command.ReadAsync("dd",
                    ["if=/dev/zero", $"of={filename}", "bs=1", "count=0", $"seek={size.ToString()}"]).GetAwaiter().GetResult();

                return Flows.Val(new Value<int>(0));
            }
            catch (ExitCodeReadException ex)
            {
                logger.Error(ex.StandardError);
                return Flows.Err<Value<int>>($"dd exited with non-zero exit code <{ex.ExitCode}>");
            }
        }

        public static bool IsDdPresent()
        {
            string path = Environment.GetEnvironmentVariable("PATH")!;

            foreach (var dir in path.Split(':'))
            {
                if (File.Exists(Path.Join(dir, "dd")))
                {
                    return true;
                }
            }

            return false;
        }
        
        public static Flow<Value<char>> ExecuteBcdBoot(char bootLetter, char dataLetter, ILog logger)
        {
            try
            {
                var args = new[] { @$"{dataLetter}:\Windows", "/s", $"{bootLetter}:", "/f", "UEFI" };
                (string stdOut, string stdErr) = Cli.SimpleExec.Command.ReadAsync("bcdboot", args).GetAwaiter().GetResult();

                logger.Log($"Executing bcdboot {string.Join(' ', args)}");
                
                if (Program.IsInteractive)
                {
                    logger.Log(stdOut);

                    if (stdErr.Length > 0)
                    {
                        logger.Error(stdErr);
                    }
                }

                return Flows.Val(new Value<char>(bootLetter));
            }
            catch (ExitCodeReadException ex)
            {
                logger.Error(ex.StandardError);
                return new($"Bcdboot exited with non-zero exit code <{ex.ExitCode}>");
            }
        }
    }
}
