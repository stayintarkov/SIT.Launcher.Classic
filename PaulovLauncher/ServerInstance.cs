using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Launcher
{
    public class ServerInstance
    {
        public static List<ServerInstance> ServerInstances = new List<ServerInstance>();

        public string ServerName { get; set; } = "Local";
        public string ServerAddress { get; set; } = "https://localhost:443";
    }
}
