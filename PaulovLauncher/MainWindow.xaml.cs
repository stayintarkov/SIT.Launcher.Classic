using MahApps.Metro.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public string ServerAddress { get; set; }

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
            if(!string.IsNullOrEmpty(returnData) && returnData != "FAILED" && returnData.StartsWith("AID"))
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Executable (EscapeFromTarkov.exe)|EscapeFromTarkov.exe;";
                if(openFileDialog.ShowDialog() == true)
                {
                    var commandArgs = $"-token={returnData} -config={{\"BackendUrl\":\"{ServerAddress}\",\"Version\":\"live\"}}";
                    Process.Start(openFileDialog.FileName, commandArgs);
                    WindowState = WindowState.Minimized;
                }
            }
        }
    }
}
