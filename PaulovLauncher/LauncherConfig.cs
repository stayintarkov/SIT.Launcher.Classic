using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Launcher
{
    public class LauncherConfig
    {

        private static LauncherConfig instance;// = new LauncherConfig();

        public static LauncherConfig Instance
        {
            get 
            {
                if (instance == null)
                    Instance = Load();

                return instance; 
            }
            set { instance = value; }
        }


        private LauncherConfig()
        {
            if (instance == null)
            {
                instance = this;
                return;
            }
        }

        public ICollection<ServerInstance> ServerInstances { get; set; }

        public ServerInstance ServerInstance { get; set; } = new ServerInstance() { ServerAddress = "https://localhost:443" };

        public string Username { get; set; }

        public bool AutomaticallyInstallAssemblyDlls { get; set; } = true;

        public bool AutomaticallyDeobfuscateDlls { get; set; } = true;

        public bool AutomaticallyInstallSIT { get; set; } = true;

        public bool AutomaticallyInstallAkiSupport { get; set; } = true;

        private static LauncherConfig Load()
        {
            LauncherConfig launcherConfig = new LauncherConfig()
            {
                AutomaticallyDeobfuscateDlls = true,
                AutomaticallyInstallAssemblyDlls = true,
                AutomaticallyInstallSIT = true,
                AutomaticallyInstallAkiSupport = true,
                ServerInstance = new ServerInstance() { ServerAddress = "https://localhost:443" }
            };
            if(File.Exists(App.ApplicationDirectory + "LauncherConfig.json"))
                launcherConfig = JsonConvert.DeserializeObject<LauncherConfig>(File.ReadAllText(App.ApplicationDirectory + "LauncherConfig.json"));

            return launcherConfig;
        }

        public void Save()
        {
            File.WriteAllText("LauncherConfig.json", JsonConvert.SerializeObject(this));
        }
    }
}
