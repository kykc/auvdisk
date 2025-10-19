using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace auvdisk
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal class CommandExecutorConfig
    {
        public string Executable { get; set; } = "";
        public List<string> Arguments { get; set; } = new List<string>();
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
        public string? WorkingDir { get; set; }
        public bool RequiresElevation { get; set; } = false;
        public bool WaitForExit { get; set; } = false;
    }
}
