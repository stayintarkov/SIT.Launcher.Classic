using FolderBrowserEx;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using Octokit;
using SIT.Launcher.DeObfus;
using SIT.Launcher.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    public partial class MainWindow : MetroWindow, ILogger
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

            this.Title = "SIT Launcher - " + App.ProductVersion.ToString();

            this.Loaded += MainWindow_Loaded;
            this.ContentRendered += MainWindow_ContentRendered;
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            NewInstallFromOfficial();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private async void NewInstallFromOfficial()
        {
            if (string.IsNullOrEmpty(Config.InstallLocation))
            {
                if (MessageBox.Show("No OFFLINE install found. Would you like to install now?", "Install", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    var fiOfficialGame = OfficialGameFinder.FindOfficialGame();
                    if (fiOfficialGame == null)
                        return;

                    //FolderBrowserDialog folderBrowserDialogOfficial = new FolderBrowserDialog();
                    //folderBrowserDialogOfficial.Title = "Select Official EFT Folder";
                    //folderBrowserDialogOfficial.InitialFolder = Directory.GetParent(OfficialGameFinder.FindOfficialGame().FullName).FullName;
                    //if (folderBrowserDialogOfficial.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    //{
                        FolderBrowserDialog folderBrowserDialogOffline = new FolderBrowserDialog();
                        folderBrowserDialogOffline.Title = "Select New Offline EFT Install Folder";
                        if (folderBrowserDialogOffline.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            if (fiOfficialGame.DirectoryName == folderBrowserDialogOffline.SelectedFolder)
                            {
                                MessageBox.Show("You cannot install OFFLINE into your Official Folder!", "Install");
                                NewInstallFromOfficial();
                                return;
                            }

                            var exeLocation = string.Empty;

                            var officialFiles = Directory
                                .GetFiles(fiOfficialGame.DirectoryName, "*", new EnumerationOptions() { RecurseSubdirectories = true })
                                .Select(x => new FileInfo(x));
                            foreach (var file in officialFiles) 
                            {
                                await loadingDialog.UpdateAsync("Installing", $"Copying file: {file.Name}");
                                var newFilePath = file.FullName.Replace(fiOfficialGame.DirectoryName, folderBrowserDialogOffline.SelectedFolder);
                                Directory.CreateDirectory(Directory.GetParent(newFilePath).FullName);

                                var fiNewFile = new FileInfo(newFilePath);
                                if (!fiNewFile.Exists || fiNewFile.LastWriteTime < file.LastWriteTime)
                                    file.CopyTo(newFilePath, true);

                                if (newFilePath.Contains("EscapeFromTarkov.exe"))
                                    exeLocation = newFilePath;
                            }

                            Config.InstallLocation = folderBrowserDialogOffline.SelectedFolder + "\\EscapeFromTarkov.exe";
                            this.DataContext = null;
                            this.DataContext = this;

                            CleanupDirectory(exeLocation);
                            UpdateButtonText(null);

                            await loadingDialog.UpdateAsync(null, null);
                        }
                    //}
                }
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Config.Save();
        }

        public LauncherConfig Config { get; } = LauncherConfig.Instance;

        public IEnumerable<ServerInstance> ServerInstances 
        { 
            get 
            {
                return ServerInstance.ServerInstances.AsEnumerable();
            } 
        }

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

        private string LoginToServer()
        {
            if (string.IsNullOrEmpty(ServerAddress))
            {
                MessageBox.Show("No Server Address Provided");
                return null;
            }

            if (ServerAddress.EndsWith("/"))
            {
                MessageBox.Show("Server Address is incorrect, you should NOT have a / at the end!");
                return null;
            }
            TarkovRequesting requesting = new TarkovRequesting(null, ServerAddress, false);

            Dictionary<string, string> data = new Dictionary<string, string>();
            data.Add("username", Username);
            data.Add("email", Username);
            data.Add("edition", "Edge Of Darkness"); // default to EoD
            //data.Add("edition", "Standard");
            if (string.IsNullOrEmpty(txtPassword.Password))
            {
                MessageBox.Show("You cannot use an empty Password for your account!");
                return null;
            }
            data.Add("password", txtPassword.Password);

            // connect and get editions
            //var returnDataConnect = requesting.PostJson("/launcher/server/connect", JsonConvert.SerializeObject(data));

            // attempt to login
            var returnData = requesting.PostJson("/launcher/profile/login", JsonConvert.SerializeObject(data));

            // If failed, attempt to register
            if (returnData == "FAILED")
            {
                var messageBoxResult = MessageBox.Show("Your account has not been found, would you like to register a new account with these credentials?", "Account", MessageBoxButton.YesNo);
                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    returnData = requesting.PostJson("/launcher/profile/register", JsonConvert.SerializeObject(data));
                    var messageBoxResultRegister = MessageBox.Show(
                        "Your account has been registered. " + Environment.NewLine +
                        "Due to an SPT-Aki error. You must start the game once, then ALT-F4 when the screen is blank, then start again to login!"
                        , "Account"
                        , MessageBoxButton.YesNo);
                }
                else
                {
                    return null;
                }
            }

            return returnData;
        }

        private async void btnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            var returnData = LoginToServer();

            if (string.IsNullOrEmpty(returnData))
            {
                var messageBoxResult = MessageBox.Show("Something went wrong. Maybe the server hasn't been started? Check the logs.", "Account");
                return;
            }

            // If all good, launch game with AID
            if (!string.IsNullOrEmpty(returnData) && returnData != "FAILED" && returnData != "ALREADY_IN_USE")
            {
                BrowseForOfflineGame();

                // Check that above actually did something
                if (!string.IsNullOrEmpty(Config.InstallLocation) && Config.InstallLocation.EndsWith(".exe"))
                {
                    await DownloadInstallAndStartGame(returnData);
                }

            }
            else if (returnData == "ALREADY_IN_USE")
            {
                var messageBoxResult = MessageBox.Show("The username/email has already been created, please use another one.", "Account");
            }
            else if (returnData.Length != 24) // NewId or something
            {
                var messageBoxResult = MessageBox.Show("Something went wrong. Maybe the server hasn't been started? Check the logs.", "Account");
            }
        }

        private void BrowseForOfflineGame()
        {
            if (string.IsNullOrEmpty(Config.InstallLocation) || !File.Exists(Config.InstallLocation))
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Executable (EscapeFromTarkov.exe)|EscapeFromTarkov.exe;";
                if (openFileDialog.ShowDialog() == true)
                {
                    var fvi = FileVersionInfo.GetVersionInfo(openFileDialog.FileName);
                    App.GameVersion = fvi.ProductVersion;
                    Config.InstallLocation = openFileDialog.FileName;

                    UpdateButtonText(null);
                }
            }
        }

        private async Task DownloadInstallAndStartGame(string sessionId)
        {
            btnLaunchGame.IsEnabled = false;

            var installLocation = Config.InstallLocation;
            await DownloadAndInstallBepInEx5(installLocation);

            await DownloadAndInstallSIT(installLocation);

           
            // Copy Aki Dlls for support
            DownloadAndInstallAki(installLocation);

            // Deobfuscate Assembly-CSharp
            if (Config.AutomaticallyDeobfuscateDlls
                && NeedsDeobfuscation(installLocation))
            {
                MessageBox.Show("Your game has not been deobfuscated and no client mods have been installed to allow OFFLINE play. Please install SIT or manually deobfuscate.");
                //if (await Deobfuscate(installLocation))
                //{
                //    StartGame(sessionId, installLocation);
                //}
                UpdateButtonText(null);
                btnLaunchGame.IsEnabled = true;
            }
            else
            {
                // Launch game
                StartGame(sessionId, installLocation);
            }
        }

        private async void StartGame(string sessionId, string installLocation)
        {
            //App.LegalityCheck();
            CleanupDirectory(installLocation);

            UpdateButtonText(null);
            btnLaunchGame.IsEnabled = true;
            var commandArgs = $"-token={sessionId} -config={{\"BackendUrl\":\"{ServerAddress}\",\"Version\":\"live\"}}";
            Process.Start(installLocation, commandArgs);
            Config.Save();
            WindowState = WindowState.Minimized;

            await Task.Delay(10000);

            //if (Config.SendInfoToDiscord)
            //    DiscordInterop.DiscordRpcClient.UpdateDetails("In Game");
            ////do
            ////{

            ////} while (Process.GetProcessesByName("EscapeFromTarkov") != null);
            //if (Config.SendInfoToDiscord)
            //    DiscordInterop.DiscordRpcClient.UpdateDetails("");
        }

        private void CleanupDirectory(string installLocation)
        {
            UpdateButtonText("Cleaning client directory");

            var battlEyeDirPath = Directory.GetParent(installLocation).FullName + "\\BattlEye";
            if (Directory.Exists(battlEyeDirPath))
            {
                Directory.Delete(battlEyeDirPath, true);
            }
            var battlEyeExePath = installLocation.Replace("EscapeFromTarkov", "EscapeFromTarkov_BE");
            if (File.Exists(battlEyeExePath))
            {
                File.Delete(battlEyeExePath);
            }
            var cacheDirPath = Directory.GetParent(installLocation).FullName + "\\cache";
            if (Directory.Exists(cacheDirPath))
            {
                Directory.Delete(cacheDirPath, true);
            }
            var consistancyInfoPath = installLocation.Replace("EscapeFromTarkov.exe", "ConsistencyInfo");
            if (File.Exists(consistancyInfoPath))
            {
                File.Delete(consistancyInfoPath);
            }
            var uninstallPath = installLocation.Replace("EscapeFromTarkov.exe", "Uninstall.exe");
            if (File.Exists(uninstallPath))
            {
                File.Delete(uninstallPath);
            }
            var logsDirPath = System.IO.Path.Combine(Directory.GetParent(installLocation).FullName, "Logs");
            if (Directory.Exists(logsDirPath))
            {
                Directory.Delete(logsDirPath, true);
            }
        }

        private async Task DownloadAndInstallBepInEx5(string exeLocation)
        {
            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = System.IO.Path.Combine(exeLocation.Replace("EscapeFromTarkov.exe", "BepInEx"));
            var bepinexWinHttpDLL = exeLocation.Replace("EscapeFromTarkov.exe", "winhttp.dll");

            var bepinexCorePath = System.IO.Path.Combine(bepinexPath, "core");
            var bepinexPluginsPath = System.IO.Path.Combine(bepinexPath, "plugins");
            

            UpdateButtonText("Installing BepInEx");
            await Task.Delay(500);

            if (!File.Exists(App.ApplicationDirectory + "\\BepInEx5.zip"))
            {
                UpdateButtonText("Downloading BepInEx");
                await Task.Delay(500);

                using (var ms = new MemoryStream())
                {
                    using (var rStream = await new HttpClient().GetStreamAsync("https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip")) // response.GetResponseStream();
                    {
                        rStream.CopyTo(ms);
                        await File.WriteAllBytesAsync(App.ApplicationDirectory + "\\BepInEx5.zip", ms.ToArray());
                    }
                }
            }

            if (Directory.Exists(bepinexCorePath) && Directory.Exists(bepinexPluginsPath) && File.Exists(bepinexWinHttpDLL))
                return;

            UpdateButtonText("Installing BepInEx");

            System.IO.Compression.ZipFile.ExtractToDirectory(App.ApplicationDirectory + "\\BepInEx5.zip", baseGamePath, true);
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

            //if(Config.SendInfoToDiscord)
            //    DiscordInterop.DiscordRpcClient.UpdateDetails(text);

        }

        private async Task DownloadAndInstallSIT(string exeLocation)
        {
            if (!Config.AutomaticallyInstallSIT)
                return;

            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (!Directory.Exists(bepinexPluginsPath))
                return;

            UpdateButtonText("Downloading SIT");

            try
            {

                var github = new GitHubClient(new ProductHeaderValue("SIT-Launcher"));
                var user = await github.User.Get("paulov-t");
                var tarkovCoreReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Core", new ApiOptions() { });
                Release latestCore = null;
                if(Config.AutomaticallyInstallSITPreRelease)
                    latestCore = tarkovCoreReleases.OrderByDescending(x => x.CreatedAt).First(x => x.Prerelease);
                else
                    latestCore = tarkovCoreReleases.OrderByDescending(x => x.CreatedAt).First(x => !x.Prerelease);

                var allAssets = latestCore.Assets.OrderByDescending(x => x.CreatedAt).DistinctBy(x => x.Name);
                var allAssetsCount = allAssets.Count();
                var assetIndex = 0;
                foreach (var A in allAssets)
                {
                    var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(A.BrowserDownloadUrl);
                    if (response != null)
                    {
                        var ms = new MemoryStream();
                        await response.Content.CopyToAsync(ms);

                        var deliveryPath = App.ApplicationDirectory + "\\ClientMods\\" + A.Name;
                        var fiDelivery = new FileInfo(deliveryPath);
                        await File.WriteAllBytesAsync(deliveryPath, ms.ToArray());
                    }
                    assetIndex++;
                }



                UpdateButtonText("Installing SIT");

                foreach (var clientModDLL in Directory.GetFiles(App.ApplicationDirectory + "\\ClientMods\\").Where(x => !x.Contains("DONOTDELETE")))
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

                        if (Config.ForceInstallLatestSIT)
                            File.Copy(clientModDLL, assemblyLocation, true);
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

                        if (Config.ForceInstallLatestSIT)
                            shouldCopy = true;

                        if (shouldCopy)
                            File.Copy(clientModDLL, bepinexPluginsPath + "\\" + fiClientMod.Name, true);
                    }
                }
            }
            catch (Exception)
            {
                var r = MessageBox.Show("Unable to download and install SIT", "Error");
            }



        }

        private string GetBepInExPath(string exeLocation)
        {
            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = System.IO.Path.Combine(exeLocation.Replace("EscapeFromTarkov.exe", "BepInEx"));
            return bepinexPath;
        }

        private string GetBepInExPluginsPath(string exeLocation)
        {
            var bepinexPluginsPath = System.IO.Path.Combine(GetBepInExPath(exeLocation), "plugins");
            return bepinexPluginsPath;
        }

        private string GetBepInExPatchersPath(string exeLocation)
        {
            var bepinexPluginsPath = System.IO.Path.Combine(GetBepInExPath(exeLocation), "patchers");
            return bepinexPluginsPath;
        }


        private void DownloadAndInstallAki(string exeLocation)
        {
            if (!Config.AutomaticallyInstallAkiSupport)
                return;

            var bepinexPluginsPath = GetBepInExPluginsPath(exeLocation);
            var bepinexPatchersPath = GetBepInExPatchersPath(exeLocation);

            // Discover where Assembly-CSharp is within the Game Folders
            var managedPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            managedPath += "EscapeFromTarkov_Data\\Managed\\";

            List<FileInfo> fiAkiManagedFiles = Directory.GetFiles(App.ApplicationDirectory + "/AkiSupport/Managed").Select(x => new FileInfo(x)).ToList();

            DirectoryInfo diManaged = new DirectoryInfo(managedPath);
            if (diManaged.Exists)
            {
                foreach(var fileInfo in fiAkiManagedFiles)
                {
                    var path = System.IO.Path.Combine(managedPath, fileInfo.Name);

                    // DO NOT OVERWRITE IF NEWER VERSION OF AKI EXISTS IN DIRECTORY
                    var existingFI = new FileInfo(path);
                    if (existingFI.Exists && existingFI.LastWriteTime > fileInfo.LastWriteTime)
                        return;

                    fileInfo.CopyTo(path, true);
                }
            }

            List<FileInfo> fiAkiBepinexPluginsFiles = Directory.GetFiles(App.ApplicationDirectory + "/AkiSupport/Bepinex/Plugins").Select(x => new FileInfo(x)).ToList();
            DirectoryInfo diBepinex = new DirectoryInfo(bepinexPluginsPath);
            if (diBepinex.Exists)
            {
                // Delete any existing plugins in BepInEx folder. They won't work with SIT.
                List<FileInfo> fiAkiExistingPlugins = Directory.GetFiles(bepinexPluginsPath).Where(x => x.StartsWith("aki-", StringComparison.OrdinalIgnoreCase)).Select(x => new FileInfo(x)).ToList();
                foreach (var fileInfo in fiAkiExistingPlugins)
                {
                    fileInfo.Delete();
                }

                // Install any compatible Plugins from SIT Launcher
                foreach (var fileInfo in fiAkiBepinexPluginsFiles)
                {
                    var existingPath = System.IO.Path.Combine(bepinexPluginsPath, fileInfo.Name);

                    // DO NOT OVERWRITE IF NEWER VERSION OF AKI EXISTS IN DIRECTORY
                    var existingFI = new FileInfo(existingPath);
                    if (existingFI.Exists && existingFI.LastWriteTime > fileInfo.LastWriteTime)
                        return;

                    fileInfo.CopyTo(existingPath, true);
                }
            }

            List<FileInfo> fiAkiBepinexPatchersFiles = Directory.GetFiles(App.ApplicationDirectory + "/AkiSupport/Bepinex/Patchers").Select(x => new FileInfo(x)).ToList();
            DirectoryInfo diBepinexPatchers = new DirectoryInfo(bepinexPatchersPath);
            if (diBepinexPatchers.Exists)
            {
                foreach (var fileInfo in fiAkiBepinexPatchersFiles)
                {
                    //var existingPath = System.IO.Path.Combine(bepinexPatchersPath, fileInfo.Name); // Patcher is causing problems
                    var existingPath = System.IO.Path.Combine(bepinexPluginsPath, fileInfo.Name);

                    // DO NOT OVERWRITE IF NEWER VERSION OF AKI EXISTS IN DIRECTORY
                    var existingFI = new FileInfo(existingPath);
                    if (existingFI.Exists && existingFI.LastWriteTime > fileInfo.LastWriteTime)
                        return;

                    fileInfo.CopyTo(existingPath, true);
                }
            }

        }

        private void OnDeobfuscateLog(string s)
        {
            //Dispatcher.Invoke(() =>
            //{
            //    txtDeobfuscateLog.Text += s + Environment.NewLine;
            //});
            Log(s);
        }

        private async Task<bool> Deobfuscate(string exeLocation, bool createBackup = true, bool overwriteExisting = true, bool doRemapping = true)
        {
            Deobfuscator.Logged.Clear();
            await Dispatcher.InvokeAsync(() =>
            {
                txtDeobfuscateLog.Text = String.Empty;
            });
            //Deobfuscator.OnLog += OnDeobfuscateLog;
            await Dispatcher.InvokeAsync(() =>
            {
                txtDeobfuscateLog.Text = String.Empty;
                OnDeobfuscateLog("--------------------------------------------------------------------------");
                OnDeobfuscateLog("Deobfuscate started!" + Environment.NewLine);
                btnDeobfuscate.IsEnabled = false;
            });
            var result = await Deobfuscator.DeobfuscateAsync(exeLocation, createBackup, overwriteExisting, doRemapping, this);
            await Dispatcher.InvokeAsync(() =>
            {
                //foreach (var logg in Deobfuscator.Logged)
                //{
                //    txtDeobfuscateLog.Text += logg + Environment.NewLine;
                //}
                btnDeobfuscate.IsEnabled = true;
            });

            var deobfuscateLogPath = "DeobfuscateLog.txt";
            if (File.Exists(deobfuscateLogPath))
                File.Delete(deobfuscateLogPath);    

            await File.WriteAllTextAsync(deobfuscateLogPath, txtDeobfuscateLog.Text);
            //Deobfuscator.OnLog -= OnDeobfuscateLog;
            return result;
        }

        private bool NeedsDeobfuscation(string exeLocation)
        {
            var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
            assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";
            return !File.Exists(assemblyLocation + ".backup");
        }

        private void ExtractDeobfuscator()
        {
            var deobfusFolder = App.ApplicationDirectory + "/DeObfus/";
            if (!File.Exists(deobfusFolder + "Deobfuscator.zip"))
                return;

            if (!File.Exists(deobfusFolder + "/de4dot/" + "de4dot.exe"))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(deobfusFolder + "Deobfuscator.zip", deobfusFolder + "/de4dot/");
                File.Delete(deobfusFolder + "Deobfuscator.zip");
            }
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

        private void CollapseAll()
        {
            gridPlay.Visibility = Visibility.Collapsed;
            gridServer.Visibility = Visibility.Collapsed;
            gridTools.Visibility = Visibility.Collapsed;
            gridSettings.Visibility = Visibility.Collapsed;
        }

        //private void btnCoopServer_Click(object sender, RoutedEventArgs e)
        //{
        //    CollapseAll();
        //    //gridCoopServer.Visibility = Visibility.Visible;
        //}

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridPlay.Visibility = Visibility.Visible;
        }

        private void btnSettingsPopup_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridSettings.Visibility = Visibility.Visible;
        }

        private void btnToToolsWindow_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridTools.Visibility = Visibility.Visible;
        }

        private async void btnDeobfuscate_Click(object sender, RoutedEventArgs e)
        {
            BrowseForOfflineGame();
            if (!string.IsNullOrEmpty(Config.InstallLocation) && Config.InstallLocation.EndsWith(".exe"))
            {
                CleanupDirectory(Config.InstallLocation);
                await Deobfuscate(Config.InstallLocation, doRemapping: true);
            }

            UpdateButtonText(null);
        }

        private void btnDeobfuscateBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DLL (Assembly-CSharp)|Assembly-CSharp*.dll;";
            if (openFileDialog.ShowDialog() == true)
            {
                Deobfuscator.DeobfuscateAssembly(openFileDialog.FileName, Directory.GetParent(openFileDialog.FileName).FullName, doRemapping: true);
            }
        }

        public async void Log(string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                txtDeobfuscateLog.Text += message + Environment.NewLine;
                txtDeobfuscateLog.ScrollToEnd();
            });
        }

        private void btnServer_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridServer.Visibility = Visibility.Visible;
        }

        private void btnServerEXE_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnServerCommand_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserEx.FolderBrowserDialog folderBrowserDialog = new FolderBrowserEx.FolderBrowserDialog();
            if(folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Process p = new Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.FileName = "CMD.exe";
                p.StartInfo.Arguments = @"\C npm i";
                p.OutputDataReceived += process_OutputDataReceived;
                p.Start();
                p.WaitForExit();


                //p.StartInfo.FileName = @"c:\node\node.exe"; //Path to node installed folder****
                //string argument = @"\\ bundle\main.js";
                //p.StartInfo.Arguments = @argument;
                //p.Start();

                //Process.Start("CMD.exe", @"/C npm i");
                //Process.Start("CMD.exe", @"/C npm run run:server");
                
            }
        }

        private void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Dispatcher.Invoke(() => {

                txtServerLog.Text += e.Data ?? e.Data;
            
            });
        }

        private void btnChangeOfflineInstallPath_Click(object sender, RoutedEventArgs e)
        {
            Config.InstallLocation = null;
            BrowseForOfflineGame();
            Config.Save();
            this.DataContext = null;
            this.DataContext = this;
        }
    }
}
