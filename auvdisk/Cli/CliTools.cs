namespace auvdisk.Cli
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class CliTools
    {
        public static void ResizeFileUnsafe(string filename, ulong size)
        {
            var config = new CommandExecutorConfig
            {
                Executable = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName,
                RequiresElevation = true,
                Arguments = ["resize-file-unsafe", filename.Replace(" ", "\\ "), size.ToString()], // TODO: proper shell escaping
                WaitForExit = true,
            };

            CommandExecutor.Execute(config);
        }

        public static void AllocateWithDd(string filename, ulong size)
        {
            var (stdOut, stdErr) = SimpleExec.Command.ReadAsync("dd",
                ["if=/dev/zero", $"of={filename}", "bs=1", "count=0", $"seek={size.ToString()}"]).GetAwaiter().GetResult();
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
    }
}
