using MahApps.Metro.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using Octokit;
using SIT.Launcher.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SIT.Launcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            ExtractDeobfuscator();

            if (File.Exists("LauncherConfig.json"))
            {
                Config = JsonConvert.DeserializeObject<LauncherConfig>(File.ReadAllText("LauncherConfig.json"));
            }
            this.DataContext = this;

        }

        public LauncherConfig Config { get; } = LauncherConfig.Instance;

        public IEnumerable<ServerInstance> ServerInstances 
        { 
            get 
            {
                return ServerInstance.ServerInstances.AsEnumerable();
            } 
        }


        //private string _serverAddress = "https://localhost:443";

        public enum ELaunchButtonState : short
        {
            Launch,
            Deob,
            BepInEx,
            Custom = short.MaxValue
        }
        public ELaunchButtonState LaunchButtonState { get; set; } = ELaunchButtonState.Launch;

        private string _launchButtonText = "Launch";

        public string LaunchButtonText 
        { 
            get 
            {
                switch (LaunchButtonState)
                {
                    case ELaunchButtonState.Launch:
                        _launchButtonText = "Launch";
                        break;
                    case ELaunchButtonState.Deob:
                        _launchButtonText = "Deobfuscating";
                        break;
                    case ELaunchButtonState.BepInEx:
                        _launchButtonText = "Installing BepInEx";
                        break;
                    case ELaunchButtonState.Custom:
                        break;
                    default:
                        _launchButtonText = LaunchButtonState.ToString();
                        break;
                }
                return _launchButtonText;
            }
            set
            {
                LaunchButtonState = ELaunchButtonState.Custom;
                _launchButtonText = value;
            }
        }

        public string Username
        {
            get
            {

                return Config.Username;
            }
        }

        public string ServerAddress { get {

                return Config.ServerInstance.ServerAddress;
            } }


        private void btnAddNewServer_Click(object sender, RoutedEventArgs e)
        {
            AddNewServerDialog addNewServerDialog = new AddNewServerDialog();
            addNewServerDialog.ShowDialog();
        }

        private void btnRemoveServer_Click(object sender, RoutedEventArgs e)
        {

        }

        private async void btnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ServerAddress))
            {
                MessageBox.Show("No Server Address Provided");
                return;
            }
            if (!ServerAddress.StartsWith("https://"))
            {
                MessageBox.Show("Server Address must be https!");
                return;
            }
            //TarkovRequesting requesting = new TarkovRequesting(null, "https://cooptarkov-server.azurewebsites.net/", false);
            //TarkovRequesting requesting = new TarkovRequesting(null, "https://192.168.0.31:7777", false);
            TarkovRequesting requesting = new TarkovRequesting(null, ServerAddress, false);

            Dictionary<string, string> data = new Dictionary<string, string>();
            data.Add("username", Username);
            data.Add("email", Username);
            data.Add("edition", "Edge Of Darkness");
            if (string.IsNullOrEmpty(txtPassword.Password))
            {
                MessageBox.Show("You cannot use an empty Password for your account!");
                return;
            }
            data.Add("password", txtPassword.Password);
            var returnData = requesting.PostJson("/launcher/profile/login", JsonConvert.SerializeObject(data));
            // If failed, attempt to register
            if(returnData == "FAILED")
            {
                var messageBoxResult = MessageBox.Show("Your account has not been found, would you like to register a new account with these credentials?", "Account", MessageBoxButton.YesNo);
                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    returnData = requesting.PostJson("/launcher/profile/register", JsonConvert.SerializeObject(data));
                }
                else
                {
                    return;
                }
            }

            // If all good, launch game with AID
            if(!string.IsNullOrEmpty(returnData) && returnData != "FAILED" && returnData != "ALREADY_IN_USE" && returnData.StartsWith("AID"))
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Executable (EscapeFromTarkov.exe)|EscapeFromTarkov.exe;";
                if(openFileDialog.ShowDialog() == true)
                {
                    DiscordInterop.DiscordRpcClient.UpdateDetails("Installing BepInEx");
                    await DownloadAndInstallBepInEx5(openFileDialog.FileName);

                    DiscordInterop.DiscordRpcClient.UpdateDetails("Installing BepInEx");
                    await DownloadAndInstallSIT(openFileDialog.FileName);

                    DiscordInterop.DiscordRpcClient.UpdateDetails("Installing Aki DLL");
                    // Copy Aki Dlls for support
                    SupportAki(openFileDialog.FileName);

                    // Deobfuscate Assembly-CSharp
                    if (Config.AutomaticallyDeobfuscateDlls 
                        && NeedsDeobfuscation(openFileDialog.FileName))
                    {
                        if(await Deobfuscate(openFileDialog.FileName))
                        {
                            StartGame(returnData, openFileDialog);
                        }
                    }
                    else
                    {
                        // Launch game
                        StartGame(returnData, openFileDialog);
                    }
                }
            }
            else if (returnData == "ALREADY_IN_USE")
            {
                var messageBoxResult = MessageBox.Show("The username/email has already been created, please use another one.", "Account");
            }
            else
            {
                var messageBoxResult = MessageBox.Show("Something went wrong.", "Account");
            }
        }

        private async void StartGame(string sessionId, OpenFileDialog openFileDialog)
        {
            var battlEyeDirPath = Directory.GetParent(openFileDialog.FileName).FullName + "\\BattlEye";
            if (Directory.Exists(battlEyeDirPath))
            {
                Directory.Delete(battlEyeDirPath, true);
            }
            var battlEyeExePath = openFileDialog.FileName.Replace("EscapeFromTarkov", "EscapeFromTarkov_BE");
            if (File.Exists(battlEyeExePath))
            {
                File.Delete(battlEyeExePath);
            }

            var commandArgs = $"-token={sessionId} -config={{\"BackendUrl\":\"{ServerAddress}\",\"Version\":\"live\"}}";
            Process.Start(openFileDialog.FileName, commandArgs);
            Config.Save();
            WindowState = WindowState.Minimized;

            await Task.Delay(10000);
            DiscordInterop.DiscordRpcClient.UpdateDetails("In Game");
            //do
            //{

            //} while (Process.GetProcessesByName("EscapeFromTarkov") != null);
            DiscordInterop.DiscordRpcClient.UpdateDetails("");
        }

        private async Task DownloadAndInstallBepInEx5(string exeLocation)
        {
            UpdateButtonText("Downloading BepInEx");
            btnLaunchGame.IsEnabled = false;

            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (Directory.Exists(bepinexPluginsPath))
                return;

            if (!File.Exists(AppContext.BaseDirectory + "\\BepInEx5.zip"))
            {
                var httpRequest = HttpWebRequest.Create("https://github.com/BepInEx/BepInEx/releases/download/v5.4.19/BepInEx_x64_5.4.19.0.zip");
                httpRequest.Method = "GET";
                var response = await httpRequest.GetResponseAsync();
                if (response != null)
                {
                    var ms = new MemoryStream();
                    var rStream = response.GetResponseStream();
                    rStream.CopyTo(ms);
                    await File.WriteAllBytesAsync(AppContext.BaseDirectory + "\\BepInEx5.zip", ms.ToArray());
                }
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(AppContext.BaseDirectory + "\\BepInEx5.zip", baseGamePath);
            if (!Directory.Exists(bepinexPluginsPath))
            {
                Directory.CreateDirectory(bepinexPluginsPath);
            }

            UpdateButtonText(null);
            btnLaunchGame.IsEnabled = true;

        }

        private void UpdateButtonText(string text)
        {
            if (!string.IsNullOrEmpty(text)) {
                LaunchButtonText = text;
                LaunchButtonState = ELaunchButtonState.Custom;
            }
            else
            {
                LaunchButtonState = ELaunchButtonState.Launch;
            }

            btnLaunchGame.Content = LaunchButtonText;
        }

        private async Task DownloadAndInstallSIT(string exeLocation)
        {
            if (!Config.AutomaticallyInstallSIT)
                return;


            UpdateButtonText("Downloading SIT");
            btnLaunchGame.IsEnabled = false;

            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (!Directory.Exists(bepinexPluginsPath))
                return;

            var github = new GitHubClient(new ProductHeaderValue("MyAmazingApp"));
            var user = await github.User.Get("paulov-t");
            Console.WriteLine(user.Followers + " folks love the paulov-t!");
            var tarkovCoreReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Tarkov.Core");
            var latestCore = tarkovCoreReleases[0];
            var tarkovSPReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Tarkov.SP");
            var latestSP = tarkovSPReleases[0];
            var allAssets = latestSP.Assets.Union(latestCore.Assets).OrderByDescending(x=>x.CreatedAt).DistinctBy(x => x.Name);
            var allAssetsCount = allAssets.Count();
            var assetIndex = 0;
            foreach (var A in allAssets)
            {
                var httpRequest = HttpWebRequest.Create(A.BrowserDownloadUrl);
                httpRequest.Method = "GET";
                var response = await httpRequest.GetResponseAsync();
                if (response != null)
                {
                    var ms = new MemoryStream();
                    var rStream = response.GetResponseStream();
                    rStream.CopyTo(ms);

                    var deliveryPath = AppContext.BaseDirectory + "\\ClientMods\\" + A.Name;
                    var fiDelivery = new FileInfo(deliveryPath);
                    await File.WriteAllBytesAsync(deliveryPath, ms.ToArray());
                }
                UpdateButtonText($"Downloading SIT ({assetIndex}/{allAssetsCount})");
                assetIndex++;
            }

            UpdateButtonText("Installing SIT");

            foreach (var clientModDLL in Directory.GetFiles(AppContext.BaseDirectory + "\\ClientMods\\"))
            {
                if (clientModDLL.Contains("Assembly-CSharp"))
                {
                    var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
                    assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";

                    // Backup the Assembly-CSharp and place the newest clean one
                    if (!File.Exists(assemblyLocation + ".backup"))
                    {
                        File.Copy(assemblyLocation, assemblyLocation + ".backup");
                        File.Copy(clientModDLL, assemblyLocation, true);
                    }
                }
                else
                {
                    bool shouldCopy = false;
                    var fiClientMod = new FileInfo(clientModDLL);
                    var fiExistingMod = new FileInfo(bepinexPluginsPath + "\\" + fiClientMod.Name);
                    if (fiExistingMod.Exists && allAssets.Any(x => x.Name == fiClientMod.Name))
                    {
                        var createdDateOfDownloadedAsset = allAssets.FirstOrDefault(x => x.Name == fiClientMod.Name).CreatedAt;
                        shouldCopy = (fiExistingMod.LastWriteTime < createdDateOfDownloadedAsset);
                    }
                    else
                        shouldCopy = true;

                    if(shouldCopy)
                        File.Copy(clientModDLL, bepinexPluginsPath + "\\" + fiClientMod.Name, true);
                }
            }

            UpdateButtonText(null);
            btnLaunchGame.IsEnabled = true;


            //if (!File.Exists(AppContext.BaseDirectory + "\\BepInEx5.zip"))
            //{
            //    var httpRequest = HttpWebRequest.Create("https://github.com/BepInEx/BepInEx/releases/download/v5.4.19/BepInEx_x64_5.4.19.0.zip");
            //    httpRequest.Method = "GET";
            //    var response = await httpRequest.GetResponseAsync();
            //    if (response != null)
            //    {
            //        var ms = new MemoryStream();
            //        var rStream = response.GetResponseStream();
            //        rStream.CopyTo(ms);
            //        await File.WriteAllBytesAsync(AppContext.BaseDirectory + "\\BepInEx5.zip", ms.ToArray());
            //    }
            //}

            //System.IO.Compression.ZipFile.ExtractToDirectory(AppContext.BaseDirectory + "\\BepInEx5.zip", baseGamePath);
            //if (!Directory.Exists(bepinexPluginsPath))
            //{
            //    Directory.CreateDirectory(bepinexPluginsPath);
            //}
        }

        private void SupportAki(string exeLocation)
        {
            // Discover where Assembly-CSharp is within the Game Folders
            var managedPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            managedPath += "EscapeFromTarkov_Data\\Managed\\";
            DirectoryInfo diManaged = new DirectoryInfo(managedPath);
            if (diManaged.Exists)
            {
                List<FileInfo> fiAkiFiles = Directory.GetFiles(AppContext.BaseDirectory + "/AkiSupport/").Select(x => new FileInfo(x)).ToList();
                foreach(var fileInfo in fiAkiFiles)
                {
                    fileInfo.CopyTo(managedPath + fileInfo.Name, true);
                }
            }
        }

        private async Task<bool> Deobfuscate(string exeLocation)
        {
            var deobfusFolder = System.AppContext.BaseDirectory + "/DeObfus/";
            var deobfusApp = deobfusFolder + "de4dot-x64.exe";

            // Discover where Assembly-CSharp is within the Game Folders
            var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
            assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";

            // Backup the Assembly-CSharp and place the newest clean one
            if (!File.Exists(assemblyLocation + ".backup"))
            {
                File.Copy(assemblyLocation, assemblyLocation + ".backup");

                List<FileInfo> fileInfos = Directory.GetFiles(AppContext.BaseDirectory + "/DeObfus/PatchedAssemblies/").Select(x => new FileInfo(x)).ToList();
                var lastAssembly = fileInfos.OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                lastAssembly.CopyTo(assemblyLocation, true);
            }
            return true;

            //// Extract the Deobfuscator zip
            //ExtractDeobfuscator();

            //// Discover if it needs Deobfuscation
            ////if (NeedsDeobfuscation(exeLocation))
            ////{
            //    // Delete any left over Assembly-CSharp.dll file
            //    if(File.Exists("Assembly-CSharp.dll"))
            //        File.Delete("Assembly-CSharp.dll");

            //    // Backup the Assembly-Csharp
            //    File.Copy(assemblyLocation, assemblyLocation + ".backup", true);
            //    // Copy the Assembly-Csharp to our folder
            //    File.Copy(assemblyLocation, "Assembly-CSharp.dll", true);

            //    var commandArgs = "--un-name \"!^<>[a-z0-9]$&!^<>[a-z0-9]__.*$&![A-Z][A-Z]\\$<>.*$&^[a-zA-Z_<{$][a-zA-Z_0-9<>{}$.`-]*$\" \"Assembly-CSharp.dll\"";
            //    LaunchButtonState = ELaunchButtonState.Deob;
            //    btnLaunchGame.IsEnabled = false;
            //    var deobfusProcess = Process.Start(deobfusApp, commandArgs);
            //    if (deobfusProcess != null)
            //    {
            //        await Task.Run(() =>
            //        {
            //            while (!deobfusProcess.HasExited) { }
            //        });
            //        LaunchButtonState = ELaunchButtonState.Launch;
            //        btnLaunchGame.IsEnabled = true;
            //        File.Copy("Assembly-CSharp-cleaned.dll", assemblyLocation, true);

            //        return true;
            //    }
            ////}
            //return false;
        }

        private bool NeedsDeobfuscation(string exeLocation)
        {
            var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
            assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";
            return !File.Exists(assemblyLocation + ".backup");
                //var deobfusFolder = System.AppContext.BaseDirectory + "/DeObfus/";
                //var deobfusApp = deobfusFolder + "de4dot-x64.exe";
                //AssemblyLoadContext loadContext = new AssemblyLoadContext("AssemblyCSharpLC",true);

                //var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
                //assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";
                //File.Copy(assemblyLocation, "Assembly-CSharp-Readable.dll", true);

                ////var assembly = loadContext.LoadFromAssemblyPath( AppContext.BaseDirectory + "\\Assembly-CSharp-Readable.dll");
                //var assembly = System.Reflection.Assembly.LoadFrom("Assembly-CSharp-Readable.dll");
                //if (assembly == null)
                //    throw new DllNotFoundException("Assembly-CSharp.dll cannot be found");

                //try
                //{
                //    var type = assembly.GetTypes().FirstOrDefault(t => t.Name == "AbstractGame");
                //    if (type == null)
                //        throw new DllNotFoundException("Could not find AbstractGame in Assembly-CSharp.dll");

                //    var methodToCheck = type.GetMethod("\\uE000", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                //    return (methodToCheck != null);
                //}
                //catch
                //{

                //}

                //loadContext.Unload();
                //Thread.Sleep(2000);
                //return true;
        }

        private void ExtractDeobfuscator()
        {
            var deobfusFolder = System.AppContext.BaseDirectory + "/DeObfus/";
            if (!File.Exists(deobfusFolder + "Deobfuscator.zip"))
                return;

            if (!File.Exists(deobfusFolder + "de4dot-x64.exe"))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(deobfusFolder + "Deobfuscator.zip", deobfusFolder);
                File.Delete(deobfusFolder + "Deobfuscator.zip");
            }
        }

        private void btnSettingsPopup_Click(object sender, RoutedEventArgs e)
        {
            Settings settings = new Settings();
            this.Close();
            settings.Show();
        }

        private void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Executable (Server.exe)|Server.exe;";
            if (openFileDialog.ShowDialog() == true)
            {
                //if(!Process.GetProcessesByName("Server").Any())
                    Process.Start(openFileDialog.FileName, "");
            }
        }
    }
}
