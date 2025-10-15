using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    internal class Vhdtool
    {
        public static void Create(string filename, ulong size)
        {
            var config = new CommandExecutorConfig
            {
                Executable = "vhdtool.exe",
                RequiresElevation = true,
                // TODO: proper shell escaping
                Arguments = new List<string> { "/create", "\"" + filename + "\"", size.ToString() },
                WaitForExit = true,
            };

            CommandExecutor.execute(config);
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
    }
}
