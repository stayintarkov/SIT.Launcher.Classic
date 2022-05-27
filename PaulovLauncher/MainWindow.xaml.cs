using MahApps.Metro.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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

namespace PaulovLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            if(File.Exists("LastUsedSettings.json"))
            {
                var ServerInstance = JsonConvert.DeserializeObject<ServerInstance>(File.ReadAllText("LastUsedSettings.json"));
                ServerAddress = ServerInstance.ServerAddress;
            }
            this.DataContext = this;

        }

        public IEnumerable<ServerInstance> ServerInstances 
        { 
            get 
            {
                return ServerInstance.ServerInstances.AsEnumerable();
            } 
        }

        public string Username { get; set; }

        private string _serverAddress = "https://localhost:443";

        public string ServerAddress
        {
            get { return _serverAddress; }
            set { _serverAddress = value; }
        }


        private void btnAddNewServer_Click(object sender, RoutedEventArgs e)
        {
            AddNewServerDialog addNewServerDialog = new AddNewServerDialog();
            addNewServerDialog.ShowDialog();
        }

        private void btnRemoveServer_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnLaunchGame_Click(object sender, RoutedEventArgs e)
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
                    var commandArgs = $"-token={returnData} -config={{\"BackendUrl\":\"{ServerAddress}\",\"Version\":\"live\"}}";
                    Process.Start(openFileDialog.FileName, commandArgs);

                    File.WriteAllText("LastUsedSettings.json", JsonConvert.SerializeObject(new ServerInstance() { ServerAddress = ServerAddress }));

                    WindowState = WindowState.Minimized;
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
    }
}
