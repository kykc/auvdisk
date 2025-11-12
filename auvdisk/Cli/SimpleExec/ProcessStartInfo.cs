using System.Text;

namespace auvdisk.Cli.SimpleExec
{
    internal static class ProcessStartInfo
    {
        public static System.Diagnostics.ProcessStartInfo Create(
            string name,
            string args,
            IEnumerable<string> argList,
            string workingDirectory,
            bool redirectStandardStreams,
            Action<IDictionary<string, string?>> configureEnvironment,
            bool createNoWindow,
            Encoding? encoding = null,
            bool asAdmin = false)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = name,
                Arguments = args,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardError = redirectStandardStreams,
                RedirectStandardInput = redirectStandardStreams,
                RedirectStandardOutput = redirectStandardStreams,
                CreateNoWindow = createNoWindow,
                StandardErrorEncoding = encoding,
                StandardOutputEncoding = encoding,
            };

            if (asAdmin)
            {
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
            }
            else
            {
                configureEnvironment(startInfo.Environment);
            }

            foreach (var arg in argList)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return startInfo;
        }
    }
}