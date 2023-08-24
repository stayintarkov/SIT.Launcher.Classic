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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;

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
            _ = UpdateInstallFromOfficial();

            _ = DisplayLatestReleaseNews();
        }

        private async Task DisplayLatestReleaseNews()
        {
            var github = new GitHubClient(new ProductHeaderValue("SIT-Launcher"));
            var user = await github.User.Get("paulov-t");
            var tarkovCoreReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Core", new ApiOptions() { });
            var tarkovCoreLatestRelease = tarkovCoreReleases.OrderByDescending(x => x.CreatedAt).First();

            rtbSITReleaseNews.Document = HtmlToFlowDocument(tarkovCoreLatestRelease.Body);
            txtSITLatestReleaseTitle.Text = tarkovCoreLatestRelease.Name + " - " + tarkovCoreLatestRelease.CreatedAt.ToString();

        }
        public static FlowDocument HtmlToFlowDocument(string text)
        {
            var document = new FlowDocument();
            using (var stream = new MemoryStream((new UTF8Encoding()).GetBytes(text)))
            {
                var txt = new TextRange(document.ContentStart, document.ContentEnd);
                txt.Load(stream, DataFormats.Text);
            }
            return document;
        }

        private async Task UpdateInstallFromOfficial()
        {
            if (!IsGameUpdateAvailable())
                return;

            MessageBoxResult messageBoxResult = MessageBox.Show("New update of Official EFT detected. Would you like to update now? (BEWARE, THIS COULD BREAK SIT!)", "EFT Update Detected", MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                var exeLocation = string.Empty;
                exeLocation = await CopyInstallFromOfficial(
                    OfficialGameFinder.FindOfficialGame()
                    , Directory.GetParent(Config.InstallLocation).FullName
                    , exeLocation);
            }

        }

        private bool IsGameUpdateAvailable()
        {
            if (string.IsNullOrEmpty(Config.InstallLocation))
                return false;

            if (OfficialGameFinder.FindOfficialGame() == null)
                return false;

            var officialAssemblyCSharpPath = new FileInfo(Path.Combine(Directory.GetParent(OfficialGameFinder.FindOfficialGame().FullName).FullName, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll"));
            if (!officialAssemblyCSharpPath.Exists)
                return false;

            var offlineAssemblyCSharpPath = new FileInfo(Path.Combine(Directory.GetParent(Config.InstallLocation).FullName, "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll"));
            if (!offlineAssemblyCSharpPath.Exists)
                return false;

            var officialIsUpdated = officialAssemblyCSharpPath.CreationTime > offlineAssemblyCSharpPath.LastWriteTime; 
            return officialIsUpdated;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private async void NewInstallFromOfficial()
        {
            // Brand new setup of SIT
            if (string.IsNullOrEmpty(Config.InstallLocation) 
                
                // Config Install Location exists, but the install location looks suspiciuosly like a direct copy of Live
                // Check BepInEx
                || !DoesBepInExExistInInstall(Config.InstallLocation)
                // Check SIT.Core
                || !IsSITCoreInstalled(Config.InstallLocation)
               
                
                )
            {
                if (MessageBox.Show("No OFFLINE install found. Would you like to install now?", "Install", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    var fiOfficialGame = OfficialGameFinder.FindOfficialGame();
                    if (fiOfficialGame == null)
                        return;

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
                        exeLocation = await CopyInstallFromOfficial(fiOfficialGame, folderBrowserDialogOffline.SelectedFolder, exeLocation);
                    }
                }
            }
        }

        private async Task<string> CopyInstallFromOfficial(FileInfo fiOfficialGame, string offlineFolder, string exeLocation)
        {
            var officialFiles = Directory
                                        .GetFiles(fiOfficialGame.DirectoryName, "*", new EnumerationOptions() { RecurseSubdirectories = true })
                                        .Select(x => new FileInfo(x));
            foreach (var file in officialFiles)
            {
                await loadingDialog.UpdateAsync("Installing", $"Copying file: {file.Name}");
                var newFilePath = file.FullName.Replace(fiOfficialGame.DirectoryName, offlineFolder);
                Directory.CreateDirectory(Directory.GetParent(newFilePath).FullName);

                var fiNewFile = new FileInfo(newFilePath);
                if (!fiNewFile.Exists || fiNewFile.LastWriteTime < file.LastWriteTime)
                    file.CopyTo(newFilePath, true);

                if (newFilePath.Contains("EscapeFromTarkov.exe"))
                    exeLocation = newFilePath;
            }

            Config.InstallLocation = offlineFolder + "\\EscapeFromTarkov.exe";
            this.DataContext = null;
            this.DataContext = this;

            // ----------------------------------------------------------------------------------------
            // Delete Deobfuscated and Backup Assembly CSharps
            await loadingDialog.UpdateAsync("Installing", $"Deleting old Assembly-CSharp backups");
            var offlineFolderFiles = Directory
                                        .GetFiles(offlineFolder, "Assembly-CSharp*", new EnumerationOptions() { RecurseSubdirectories = true })
                                        .Select(x => new FileInfo(x));
            foreach (var file in offlineFolderFiles)
            {
                if(file.FullName.EndsWith(".backup") || file.FullName.EndsWith("-cleaned.dll"))
                    file.Delete();
            }
            //
            // ----------------------------------------------------------------------------------------

            await loadingDialog.UpdateAsync("Installing", $"Cleaning EFT OFFLINE Directory");
            CleanupDirectory(exeLocation);
           
            await loadingDialog.UpdateAsync("Installing", $"Installing BepInEx");
            await DownloadAndInstallBepInEx5(exeLocation);
           
            await loadingDialog.UpdateAsync("Installing", $"Installing SIT.Core");
            await DownloadAndInstallSIT(exeLocation);

          

            UpdateButtonText(null);

            await loadingDialog.UpdateAsync(null, null);
            return exeLocation;
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

            try
            {
                // attempt to login
                var returnData = requesting.PostJson("/launcher/profile/login", JsonConvert.SerializeObject(data));

                // If failed, attempt to register
                if (returnData == "FAILED")
                {
                    var messageBoxResult = MessageBox.Show("Your account has not been found, would you like to register a new account with these credentials?", "Account", MessageBoxButton.YesNo);
                    if (messageBoxResult == MessageBoxResult.Yes)
                    {
                        // Register
                        returnData = requesting.PostJson("/launcher/profile/register", JsonConvert.SerializeObject(data));
                        // Login attempt after register
                        returnData = requesting.PostJson("/launcher/profile/login", JsonConvert.SerializeObject(data));

                    }
                    else
                    {
                        return null;
                    }
                }

                return returnData;
            }
            catch (System.Net.WebException webEx)
            {
                MessageBox.Show(webEx.Message, "Unable to communicate with the Server");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to communicate with the Server");
            }
            return null;
        }

        private async void btnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            Config.Save();

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

            if(Config.CloseLauncherAfterLaunch)
            {
                App.Current.Shutdown();
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
            if(!await DownloadAndInstallBepInEx5(installLocation))
            {
                MessageBox.Show("Install and Start aborted");
                return;
            }

            if (!await DownloadAndInstallSIT(installLocation))
            {
                MessageBox.Show("Install and Start aborted");
                return;
            }

            // Copy Aki Dlls for support
            if (!DownloadAndInstallAki(installLocation))
            {
                MessageBox.Show("Install and Start aborted");
                return;
            }
            

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

        private bool DoesBepInExExistInInstall(string exeLocation)
        {
            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = System.IO.Path.Combine(exeLocation.Replace("EscapeFromTarkov.exe", "BepInEx"));
            var bepinexWinHttpDLL = exeLocation.Replace("EscapeFromTarkov.exe", "winhttp.dll");

            var bepinexCorePath = System.IO.Path.Combine(bepinexPath, "core");
            var bepinexPluginsPath = System.IO.Path.Combine(bepinexPath, "plugins");

            return (Directory.Exists(bepinexCorePath) && Directory.Exists(bepinexPluginsPath) && File.Exists(bepinexWinHttpDLL));
        }

        private async Task<bool> DownloadAndInstallBepInEx5(string exeLocation)
        {
            try
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
                        var httpClient = new HttpClient();
                        httpClient.Timeout = new TimeSpan(0, 1, 0);
                        using (var rStream = await httpClient.GetStreamAsync("https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip")) // response.GetResponseStream();
                        {
                            rStream.CopyTo(ms);
                            await File.WriteAllBytesAsync(App.ApplicationDirectory + "\\BepInEx5.zip", ms.ToArray());
                        }
                    }
                }

                if (DoesBepInExExistInInstall(exeLocation))
                    return true;

                UpdateButtonText("Installing BepInEx");

                System.IO.Compression.ZipFile.ExtractToDirectory(App.ApplicationDirectory + "\\BepInEx5.zip", baseGamePath, true);
                if (!Directory.Exists(bepinexPluginsPath))
                {
                    Directory.CreateDirectory(bepinexPluginsPath);
                }
            }
            catch(Exception ex) 
            { 
                MessageBox.Show($"Unable to Install BepInEx: {ex.Message}", "Error");
                return false;
            }

            UpdateButtonText(null);
            btnLaunchGame.IsEnabled = true;
            return true;

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

        private bool IsSITCoreInstalled(string exeLocation)
        {
            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (!Directory.Exists(bepinexPluginsPath))
                return false;

            return File.Exists(bepinexPluginsPath + "SIT.Core.dll");
        }

        private async Task<bool> DownloadAndInstallSIT(string exeLocation, bool forceInstall = false)
        {
            if (!Config.AutomaticallyInstallSIT && IsSITCoreInstalled(exeLocation))
                return true;

            await loadingDialog.UpdateAsync("Installing SIT", $"Disovering files");

            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (!Directory.Exists(bepinexPluginsPath))
                return false;

       
            UpdateButtonText("Downloading SIT");

            try
            {

                var github = new GitHubClient(new ProductHeaderValue("SIT-Launcher"));
                var user = await github.User.Get("paulov-t");
                var tarkovCoreReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Core", new ApiOptions() { });
                var tarkovCoreReleasesOrdered = tarkovCoreReleases.OrderByDescending(x => x.CreatedAt).ToList();
                Release latestCore = null;
                if ((Config.AutomaticallyInstallSITPreRelease || Config.ForceInstallLatestSIT) && tarkovCoreReleasesOrdered[0].Prerelease)
                    latestCore = tarkovCoreReleasesOrdered[0];

                if (latestCore == null)
                    latestCore = tarkovCoreReleasesOrdered.First(x => !x.Prerelease);

                var clientModsDeliveryPath = Path.Combine(App.ApplicationDirectory, "ClientMods");
                Directory.CreateDirectory(clientModsDeliveryPath);

                var maxSize = 100000000;
                var allAssets = latestCore
                    .Assets
                    .Where(x => x.Size < maxSize)
                    .OrderByDescending(x => x.CreatedAt).DistinctBy(x => x.Name);

                await loadingDialog.UpdateAsync("Installing SIT", $"Downloading files");

                var allAssetsCount = allAssets.Count();
                var assetIndex = 0;

                var httpClient = new HttpClient();
                httpClient.Timeout = new TimeSpan(0, 5, 0);

                foreach (var asset in allAssets)
                {
                    var response = await httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseContentRead);
                    if (response != null)
                    {
                        var ms = new MemoryStream();
                        await response.Content.CopyToAsync(ms);

                        var deliveryPath = Path.Combine(clientModsDeliveryPath, asset.Name); //App.ApplicationDirectory + "\\ClientMods\\" + asset.Name;
                        var fiDelivery = new FileInfo(deliveryPath);
                        await File.WriteAllBytesAsync(deliveryPath, ms.ToArray());
                    }
                    assetIndex++;
                    await loadingDialog.UpdateAsync("Installing SIT", $"Downloading files ({assetIndex}/{allAssetsCount})");
                }


                await loadingDialog.UpdateAsync("Installing SIT", $"Installing files");

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

                File.WriteAllText("CurrentSITVersion.txt", latestCore.Name);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to download and install SIT.{Environment.NewLine} {ex.Message}", "Error");
                return false;
            }

            await loadingDialog.UpdateAsync(null, null);

            return true;


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


        private bool DownloadAndInstallAki(string exeLocation)
        {
            if (!Config.AutomaticallyInstallAkiSupport)
                return true;

            Directory.CreateDirectory(App.ApplicationDirectory + "/AkiSupport/Bepinex/Patchers");
            Directory.CreateDirectory(App.ApplicationDirectory + "/AkiSupport/Bepinex/Plugins");
            Directory.CreateDirectory(App.ApplicationDirectory + "/AkiSupport/Managed");

            try
            {

                var bepinexPluginsPath = GetBepInExPluginsPath(exeLocation);
                var bepinexPatchersPath = GetBepInExPatchersPath(exeLocation);

                // Discover where Assembly-CSharp is within the Game Folders
                var managedPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
                managedPath += "EscapeFromTarkov_Data\\Managed\\";

                var sitLauncherAkiSupportManagedPath = App.ApplicationDirectory + "/AkiSupport/Managed";
                var sitLauncherAkiSupportBepinexPluginsPath = App.ApplicationDirectory + "/AkiSupport/Bepinex/Plugins";
                var sitLauncherAkiSupportBepinexPatchersPath = App.ApplicationDirectory + "/AkiSupport/Bepinex/Patchers";
                DirectoryInfo diAkiSupportManaged = new DirectoryInfo(sitLauncherAkiSupportManagedPath);
                DirectoryInfo diManaged = new DirectoryInfo(managedPath);

                if (diManaged.Exists && diAkiSupportManaged.Exists)
                {
                    List<FileInfo> fiAkiManagedFiles = Directory.GetFiles(sitLauncherAkiSupportManagedPath).Select(x => new FileInfo(x)).ToList();
                    foreach (var fileInfo in fiAkiManagedFiles)
                    {
                        var path = System.IO.Path.Combine(managedPath, fileInfo.Name);

                        // DO NOT OVERWRITE IF NEWER VERSION OF AKI EXISTS IN DIRECTORY
                        var existingFI = new FileInfo(path);
                        if (existingFI.Exists && existingFI.LastWriteTime > fileInfo.LastWriteTime)
                            continue;

                        fileInfo.CopyTo(path, true);
                    }
                }

                DirectoryInfo diAkiSupportBepinexPlugins = new DirectoryInfo(sitLauncherAkiSupportBepinexPluginsPath);
                DirectoryInfo diBepinex = new DirectoryInfo(bepinexPluginsPath);
                if (diBepinex.Exists && diAkiSupportBepinexPlugins.Exists)
                {
                    List<FileInfo> fiAkiBepinexPluginsFiles = Directory.GetFiles(sitLauncherAkiSupportBepinexPluginsPath).Select(x => new FileInfo(x)).ToList();

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
                            continue;

                        fileInfo.CopyTo(existingPath, true);
                    }
                }

                List<FileInfo> fiAkiBepinexPatchersFiles = Directory.GetFiles(sitLauncherAkiSupportBepinexPatchersPath).Select(x => new FileInfo(x)).ToList();
                DirectoryInfo diBepinexPatchers = new DirectoryInfo(bepinexPatchersPath);
                DirectoryInfo diAkiSupportBepinexPatchersPlugins = new DirectoryInfo(sitLauncherAkiSupportBepinexPatchersPath);
                if (diBepinexPatchers.Exists && diAkiSupportBepinexPatchersPlugins.Exists)
                {
                    foreach (var fileInfo in fiAkiBepinexPatchersFiles)
                    {
                        //var existingPath = System.IO.Path.Combine(bepinexPatchersPath, fileInfo.Name); // Patcher is causing problems
                        var existingPath = System.IO.Path.Combine(bepinexPluginsPath, fileInfo.Name);

                        // DO NOT OVERWRITE IF NEWER VERSION OF AKI EXISTS IN DIRECTORY
                        var existingFI = new FileInfo(existingPath);
                        if (existingFI.Exists && existingFI.LastWriteTime > fileInfo.LastWriteTime)
                            continue;

                        fileInfo.CopyTo(existingPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to download and install Aki.{Environment.NewLine} {ex.Message}", "Error");
                return false;
            }

            return true;

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
                btnDeobfuscateOnly.IsEnabled = false;
            });
            var result = await Deobfuscator.DeobfuscateAsync(exeLocation, createBackup, overwriteExisting, doRemapping, this);
            await Dispatcher.InvokeAsync(() =>
            {
                //foreach (var logg in Deobfuscator.Logged)
                //{
                //    txtDeobfuscateLog.Text += logg + Environment.NewLine;
                //}
                btnDeobfuscate.IsEnabled = true;
                btnDeobfuscateOnly.IsEnabled = true;
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
            gridSITVersions.Visibility = Visibility.Collapsed;

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

        private async void btnDeobfuscateOnly_Click(object sender, RoutedEventArgs e)
        {
            BrowseForOfflineGame();
            if (!string.IsNullOrEmpty(Config.InstallLocation) && Config.InstallLocation.EndsWith(".exe"))
            {
                CleanupDirectory(Config.InstallLocation);
                await Deobfuscate(Config.InstallLocation, doRemapping: false);
            }

            UpdateButtonText(null);
        }

        private void btnSITVersions_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridSITVersions.Visibility = Visibility.Visible;
        }
    }
}
