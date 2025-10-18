using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    internal static class CommandExecutor
    {
        public static int Execute(CommandExecutorConfig config)
        {
            //string arguments = config.Arguments.Select(x => Environment.ExpandEnvironmentVariables(x)).Aggregate((x, y) => x + " " + y);
            string arguments = String.Join(' ', config.Arguments.Select(x => Environment.ExpandEnvironmentVariables(x)));

            var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(config.Executable), arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = config.WorkingDir,
                RedirectStandardError = false,
                RedirectStandardOutput = false,
                RedirectStandardInput = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Minimized,
            };

            foreach (var envVar in config.Environment.Keys)
            {
                psi.EnvironmentVariables[envVar] = Environment.ExpandEnvironmentVariables(config.Environment[envVar]);
            }

            // Not Windows 2003, we need to work properly and elevate
            if (config.RequiresElevation && Environment.OSVersion.Version.Major >= 6)
            {
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Normal;
            }

            using (var runningProcess = new Process())
            {
                runningProcess.StartInfo = psi;
                runningProcess.Start();

                if (config.WaitForExit)
                {
                    runningProcess.WaitForExit();
                }

                if (runningProcess.HasExited)
                {
                    return runningProcess.ExitCode;
                }
            }

            return 0;
        }
    }
}
