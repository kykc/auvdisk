using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SimpleExec;

namespace auvdisk.Cli.SimpleExec
{
    // Original author of SimpleExec decided not to implement "run as admin"
    // I suppose because it implicates shellexec (and unability to set up env vars)
    // So I butchered the original implementation and added an ability to do this
    // I feel dirty
    public static class Command
    {
        private static readonly Action<IDictionary<string, string?>> DefaultAction = _ => { };
        private static readonly string DefaultEchoPrefix = Assembly.GetEntryAssembly()?.GetName().Name ?? "SimpleExec";
        
        public static Task RunAsync(
            string name,
            IEnumerable<string> args,
            string workingDirectory = "",
            bool noEcho = false,
            string? echoPrefix = null,
            Action<IDictionary<string, string?>>? configureEnvironment = null,
            bool createNoWindow = false,
            Func<int, bool>? handleExitCode = null,
            bool cancellationIgnoresProcessTree = false,
            bool asAdmin = false,
            CancellationToken cancellationToken = default) =>
            ProcessStartInfo
                .Create(
                    Resolve(Validate(name)),
                    "",
                    args ?? throw new ArgumentNullException(nameof(args)),
                    workingDirectory,
                    false,
                    configureEnvironment ?? DefaultAction,
                    createNoWindow,
                    asAdmin: asAdmin)
                .RunAsync(noEcho, echoPrefix ?? DefaultEchoPrefix, handleExitCode, cancellationIgnoresProcessTree, cancellationToken);

        private static async Task RunAsync(
            this System.Diagnostics.ProcessStartInfo startInfo,
            bool noEcho,
            string echoPrefix,
            Func<int, bool>? handleExitCode,
            bool cancellationIgnoresProcessTree,
            CancellationToken cancellationToken)
        {
            using var process = new Process();
            process.StartInfo = startInfo;

            await process.RunAsync(noEcho, echoPrefix, cancellationIgnoresProcessTree, cancellationToken).ConfigureAwait(false);

            if (!(handleExitCode?.Invoke(process.ExitCode) ?? false) && process.ExitCode != 0)
            {
                throw new ExitCodeException(process.ExitCode);
            }
        }
        
        public static Task<(string StandardOutput, string StandardError)> ReadAsync(
            string name,
            IEnumerable<string> args,
            string workingDirectory = "",
            Action<IDictionary<string, string?>>? configureEnvironment = null,
            Encoding? encoding = null,
            Func<int, bool>? handleExitCode = null,
            string? standardInput = null,
            bool cancellationIgnoresProcessTree = false,
            CancellationToken cancellationToken = default) => global::SimpleExec.Command.ReadAsync(
                name, 
                args, 
                workingDirectory, 
                configureEnvironment, 
                encoding, 
                handleExitCode, 
                standardInput, 
                cancellationIgnoresProcessTree, 
                cancellationToken);
        
        private static string Validate(string name) =>
            string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("The command name is missing.", nameof(name))
                : name;

        private static string Resolve(string name)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Path.IsPathRooted(name))
            {
                return name;
            }

            var extension = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(extension) && extension != ".cmd" && extension != ".bat")
            {
                return name;
            }

            var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD";

            var windowsExecutableExtensions = pathExt.Split(';')
                .Select(ext => ext.TrimStart('.'))
                .Where(ext =>
                    string.Equals(ext, "exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, "bat", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, "cmd", StringComparison.OrdinalIgnoreCase));

            var searchFileNames = string.IsNullOrEmpty(extension)
                ? windowsExecutableExtensions.Select(ex => Path.ChangeExtension(name, ex)).ToList()
#if NET8_0_OR_GREATER
                : [name];
#else
                : new List<string> { name, };
#endif

            var path = GetSearchDirectories().SelectMany(_ => searchFileNames, Path.Combine)
                .FirstOrDefault(File.Exists);

            return path == null || Path.GetExtension(path) == ".exe" ? name : path;
        }
    
        private static IEnumerable<string> GetSearchDirectories()
        {
            var currentProcessPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(currentProcessPath))
            {
                var currentProcessDirectory = Path.GetDirectoryName(currentProcessPath);
                if (!string.IsNullOrEmpty(currentProcessDirectory))
                {
                    yield return currentProcessDirectory;
                }
            }

            yield return Directory.GetCurrentDirectory();

            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            foreach (var directory in path.Split(Path.PathSeparator))
            {
                yield return directory;
            }
        }
    }
}