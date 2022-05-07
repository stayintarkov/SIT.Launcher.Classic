using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaulovLauncher
{
    public class ServerInstance
    {
        public static List<ServerInstance> ServerInstances = new List<ServerInstance>();

        public string ServerName { get; set; }
        public string ServerAddress { get; set; }
    }
}
