using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaulovLauncher
{
    public class LauncherConfig
    {
        public ICollection<ServerInstance> ServerInstances { get; set; }

        public ServerInstance ServerInstance { get; set; }

        public string Username { get; set; }

        public bool AutomaticallyDeobfuscateDlls { get; set; }
    }
}
