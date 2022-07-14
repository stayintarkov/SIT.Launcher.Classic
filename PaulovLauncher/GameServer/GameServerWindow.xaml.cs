using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

namespace SIT.Launcher.GameServer
{
    /// <summary>
    /// Interaction logic for GameServerWindow.xaml
    /// </summary>
    public partial class GameServerWindow : UserControl
    {

        public LauncherConfig Config { get; } = LauncherConfig.Instance;

        public GameServerWindow()
        {
            InitializeComponent();

            if (!Config.EnableCoopServer)
                return;

            gameServer = new EchoGameServer();
            gameServer.OnConnectionReceived += GameServer_OnConnectionReceived;
            gameServer.OnLog += GameServer_OnLog;
            gameServer.OnResetServer += GameServer_OnResetServer;
            gameServer.OnMethodCall += GameServer_OnMethodCall;
            gameServer.CreateListenersAndStart();
            SetupHeaderText();
            PerSecondUpdate();
        }

        public void SetupHeaderText()
        {
            txtHeaderInfo.Text = "Server:" + gameServer.InstanceId.ToString();
        }

        public async void PerSecondUpdate()
        {
            await Task.Run(async () =>
            {
                while (true)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtConnections.Text = string.Empty;
                        foreach (var con in EchoGameServer.Instance.ConnectedClients.Keys)
                        {
                            string accountId = string.Empty;
                            if (EchoGameServer.Instance.PingTimes.ContainsKey(con)
                                && EchoGameServer.Instance.PongTimes.ContainsKey(con)
                                && EchoGameServer.Instance.ConnectedClients.TryGetValue(con, out accountId)
                                )
                            {
                                var roundTripTime = (EchoGameServer.Instance.PongTimes[con] - EchoGameServer.Instance.PingTimes[con]);
                                //Console.WriteLine(con.ToString() + $" ({roundTripTime})");
                                var roundTripTimeInMS = roundTripTime.TotalMilliseconds < 0 ? roundTripTime.TotalMilliseconds * -1 : roundTripTime.TotalMilliseconds;
                                if (roundTripTimeInMS < 999)
                                {

                                    var isHost = EchoGameServer.Instance.HostConnection.HasValue && EchoGameServer.Instance.HostConnection.Value.Item1 == con;

                                    txtConnections.Text += $"{con} {(isHost ? "host" : "")} ({(roundTripTimeInMS > 0 ? Math.Round(roundTripTimeInMS) : 0)}ms)" + Environment.NewLine;

                                }
                            }
                            else
                            {
                            }
                        }
                    });
                    await Task.Delay(1000);
                }
            });
        }

        private void GameServer_OnMethodCall(ConcurrentDictionary<string, int> actionCounts)
        {
            Dispatcher.Invoke(() =>
            {
                txtMethodCalls.Text = String.Empty;
                foreach (var action in actionCounts)
                {
                    txtMethodCalls.Text += action.Key + "::" + action.Value + Environment.NewLine;
                }
            });
        }

        private void GameServer_OnResetServer()
        {
            Dispatcher.Invoke(() =>
            {
                SetupHeaderText();
                txtConnections.Text = String.Empty;
                txtMethodCalls.Text = String.Empty;
                txtLog.Text = String.Empty;
            });
        }

        private void GameServer_OnLog(string text)
        {
            AddToLog(text);
        }

        private void GameServer_OnConnectionReceived(IPEndPoint endPoint)
        {
            Dispatcher.Invoke(() =>
            {
                Connections.Add(endPoint.Address.ToString());
                foreach (var connection in Connections)
                {
                    txtConnections.Text += connection.ToString() + Environment.NewLine;
                }
            });
        }

        EchoGameServer gameServer { get; set; }

        List<string> Connections = new List<string>();
        Queue<string> Log = new Queue<string>();

        private void AddToLog(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (Log.Count > 10)
                {
                    Log.TryDequeue(out _);
                }
                Log.Enqueue(DateTime.Now.ToShortTimeString() + " " + text);

                txtLog.Text = string.Empty;
                foreach (var item in Log)
                {
                    txtLog.Text += item + Environment.NewLine;
                }
            });
        }
    }
}
