namespace auvdisk.Cli
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static class CliTools
    {
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
