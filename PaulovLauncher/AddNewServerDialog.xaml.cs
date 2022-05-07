using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PaulovLauncher
{
    /// <summary>
    /// Interaction logic for AddNewServerDialog.xaml
    /// </summary>
    public partial class AddNewServerDialog : MetroWindow
    {
        public ServerInstance Server { get; set; }
        public AddNewServerDialog()
        {
            InitializeComponent();
            Server = new ServerInstance();
            this.DataContext = this;
        }

        private void btnAddServer_Click(object sender, RoutedEventArgs e)
        {
            ServerInstance.ServerInstances.Add(Server);
        }
    }
}
