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
                return AppContext.BaseDirectory + "\\";//  Directory.GetParent(Process.GetCurrentProcess().MainModule.FileName).FullName + "\\";
            }
        }
        public LauncherConfig Config { get; } = LauncherConfig.Instance;


        public App()
        {
            if(Config.SendInfoToDiscord)
                new DiscordInterop().StartDiscordClient("V." + ProductVersion);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MyHandler);

            base.OnStartup(e);
        }

        private void MyHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;

            if (File.Exists("ErrorLogging.txt"))
                File.Delete("ErrorLogging.txt");

            using (StreamWriter stream = new StreamWriter("ErrorLogging.txt"))
            {
                stream.WriteLine(e.ToString());
            }

            Trace.WriteLine(e.ToString());
            Console.WriteLine(e.ToString());
            Debug.WriteLine(e.ToString());

            MessageBoxResult result = MessageBox.Show(e.ToString());
        }

    }
}
