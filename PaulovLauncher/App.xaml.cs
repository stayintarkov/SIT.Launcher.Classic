using Microsoft.Win32;
using SIT.Launcher;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SIT.Launcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static string GameVersion { get; set; }

        public static string ProductVersion
        {
            get
            {
                return Assembly.GetEntryAssembly().GetName().Version.ToString();
            }
        }

        public static string ApplicationDirectory
        {
            get
            {
                return Directory.GetParent(Process.GetCurrentProcess().MainModule.FileName).FullName + "\\";
            }
        }
        public LauncherConfig Config { get; } = LauncherConfig.Instance;


        public App()
        {
            if (Config.SendInfoToDiscord)
            {
                _ = new DiscordInterop().StartDiscordClient("V." + ProductVersion);
            }

            LegalityCheck();
        }


        internal static bool LegalityCheck()
        {
            byte[] w1 = new byte[198] { 79, 102, 102, 105, 99, 105, 97, 108, 32, 71, 97, 109, 101, 32, 110, 111, 116, 32, 102, 111, 117, 110, 100, 44, 32, 119, 101, 32, 119, 105, 108, 108, 32, 98, 101, 32, 112, 114, 111, 109, 112, 116, 105, 110, 103, 32, 116, 104, 105, 115, 32, 109, 101, 115, 115, 97, 103, 101, 32, 101, 97, 99, 104, 32, 108, 97, 117, 110, 99, 104, 44, 32, 117, 110, 108, 101, 115, 115, 32, 121, 111, 117, 32, 103, 101, 116, 32, 111, 102, 102, 105, 99, 105, 97, 108, 32, 103, 97, 109, 101, 46, 32, 87, 101, 32, 108, 111, 118, 101, 32, 116, 111, 32, 115, 117, 112, 112, 111, 114, 116, 32, 111, 102, 102, 105, 99, 105, 97, 108, 32, 99, 114, 101, 97, 116, 111, 114, 115, 32, 115, 111, 32, 109, 97, 107, 101, 32, 115, 117, 114, 101, 32, 116, 111, 32, 103, 101, 116, 32, 111, 102, 102, 105, 99, 105, 97, 108, 32, 103, 97, 109, 101, 32, 97, 108, 115, 111, 46, 32, 74, 117, 115, 116, 69, 109, 117, 84, 97, 114, 107, 111, 118, 32, 84, 101, 97, 109, 46 };
            byte[] w2 = new byte[23] { 78, 111, 32, 79, 102, 102, 105, 99, 105, 97, 108, 32, 71, 97, 109, 101, 32, 70, 111, 117, 110, 100, 33 };
            try
            {
                List<byte[]> varList = new List<byte[]>() {
                    //Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov
                    new byte[80] { 83, 111, 102, 116, 119, 97, 114, 101, 92, 87, 111, 119, 54, 52, 51, 50, 78, 111, 100, 101, 92, 77, 105, 99, 114, 111, 115, 111, 102, 116, 92, 87, 105, 110, 100, 111, 119, 115, 92, 67, 117, 114, 114, 101, 110, 116, 86, 101, 114, 115, 105, 111, 110, 92, 85, 110, 105, 110, 115, 116, 97, 108, 108, 92, 69, 115, 99, 97, 112, 101, 70, 114, 111, 109, 84, 97, 114, 107, 111, 118 },
                    //InstallLocation
                    new byte[15] { 73, 110, 115, 116, 97, 108, 108, 76, 111, 99, 97, 116, 105, 111, 110 },
                    //DisplayVersion
                    new byte[14] { 68, 105, 115, 112, 108, 97, 121, 86, 101, 114, 115, 105, 111, 110 },
                    //EscapeFromTarkov.exe
                    new byte[20] { 69, 115, 99, 97, 112, 101, 70, 114, 111, 109, 84, 97, 114, 107, 111, 118, 46, 101, 120, 101 }
                };
                //@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov"
                RegistryKey key = Registry.LocalMachine.OpenSubKey(Encoding.ASCII.GetString(varList[0]));
                if (key != null)
                {
                    //"InstallLocation"
                    object path = key.GetValue(Encoding.ASCII.GetString(varList[1]));
                    //"DisplayVersion"
                    object version = key.GetValue(Encoding.ASCII.GetString(varList[2]));
                    if (path != null && version != null)
                    {
                        var foundGameFiles = path.ToString();
                        var foundGameVersions = version.ToString();
                        string gamefilepath = Path.Combine(foundGameFiles, Encoding.ASCII.GetString(varList[3]));
                        if (LC1A(gamefilepath))
                        {
                            if (LC2B(gamefilepath))
                            {
                                return LC3C(gamefilepath);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            MessageBox.Show("Unable to detect legal copy of Escape from Tarkov. Buy and install the game before using SIT.");
            Application.Current.Shutdown();
            return false;
        }

        internal static bool LC1A(string gfp)
        {
            var fiGFP = new FileInfo(gfp);
            return (fiGFP.Exists && fiGFP.Length >= 647 * 1000);
        }

        internal static bool LC2B(string gfp)
        {
            var fiBE = new FileInfo(gfp.Replace(".exe", "_BE.exe"));
            return (fiBE.Exists && fiBE.Length >= 1024000);
        }

        internal static bool LC3C(string gfp)
        {
            var diBattlEye = new DirectoryInfo(gfp.Replace("EscapeFromTarkov.exe", "BattlEye"));
            return (diBattlEye.Exists);
        }
    }
}
