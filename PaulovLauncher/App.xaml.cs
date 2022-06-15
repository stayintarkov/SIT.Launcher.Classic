using SIT.Launcher;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace SIT.Launcher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
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

        public App()
        {
            _ = new DiscordInterop().StartDiscordClient("V." + ProductVersion);
        }
    }
}
