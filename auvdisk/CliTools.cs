using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    internal static class CliTools
    {
        public static void CreateWithVhdTool(string filename, ulong size)
        {
            var config = new CommandExecutorConfig
            {
                Executable = "vhdtool.exe",
                RequiresElevation = true,
                Arguments = new List<string> { "/create", "\"" + filename + "\"", size.ToString() }, // TODO: proper shell escaping
                WaitForExit = true,
            };

            CommandExecutor.Execute(config);
        }

        public static void AllocateWithDd(string filename, ulong size)
        {
            var config = new CommandExecutorConfig
            {
                Executable = "dd",
                RequiresElevation = false,
                Arguments = new List<string> { "if=/dev/zero", $"of={filename.Replace(" ", "\\ ")} ", "bs=1", "count=0", $"seek={size.ToString()}" }, // TODO: proper shell escaping
                WaitForExit = true
            };

            CommandExecutor.Execute(config);
        }

        // TODO: search in the executable path as well?
        public static bool IsVhdToolPresent()
        {
            string path = Environment.GetEnvironmentVariable("PATH")!;
            
            foreach (var dir in path.Split(';'))
            {
                if (File.Exists(Path.Join(dir, "vhdtool.exe")))
                {
                    return true;
                }
            }

            return false;
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
