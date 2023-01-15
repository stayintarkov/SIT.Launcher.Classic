using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SIT.Launcher.GameServer
{

    public class EchoGameServer
    {
        public LauncherConfig Config { get; } = LauncherConfig.Instance;

        public delegate void ConnectionReceivedHandler(IPEndPoint endPoint);
        public event ConnectionReceivedHandler OnConnectionReceived;

        public delegate void ResetServerHandler();
        public event ResetServerHandler OnResetServer;

        public delegate void LogHandler(string text);
        public event LogHandler OnLog;

        public delegate void MethodCallHandler(ConcurrentDictionary<string, int> actionCounts);
        public event MethodCallHandler OnMethodCall;

        public static EchoGameServer Instance { get; private set; }// = new EchoGameServer();
        //public static List<EchoGameServer> Instances = new List<EchoGameServer>();

        public static int HighestAcceptablePing { get { return 999; } }
        public TcpListener tcpServer { get; set; }
        public List<UdpClient> udpReceivers;
        public int CurrentReceiverIndex = 0;
        public int NumberOfReceivers = 1; // Two Channels. Reliable and Unreliable
        public int udpReceiverPort = 7070;
        public DateTime StartupTime = DateTime.Now;
        public bool quit;
        public int NumberOfConnections = 0;
        public (IPEndPoint, string)? HostConnection;
        public ConcurrentDictionary<string, string> ConnectedClientsIPs { get; } = new ConcurrentDictionary<string, string>();
        public ConcurrentDictionary<IPEndPoint, string> ConnectedClients { get; } = new ConcurrentDictionary<IPEndPoint, string>();
        public ConcurrentDictionary<IPEndPoint, (UdpClient, int)> ConnectedClientToReceiverPort { get; } = new ConcurrentDictionary<IPEndPoint, (UdpClient, int)>();
        public ConcurrentDictionary<IPEndPoint, DateTime> ConnectedClientsLastTimeDataReceiver { get; } = new ConcurrentDictionary<IPEndPoint, DateTime>();
        public ConcurrentDictionary<string, IPEndPoint> PlayersToConnectedClients { get; } = new ConcurrentDictionary<string, IPEndPoint>();
        public ConcurrentDictionary<string, int> MethodCallCounts { get; } = new ConcurrentDictionary<string, int>();
        public int TotalNumberOfBytesSentLastSecond = 0;
        public int TotalNumberOfBytesProcessedLastSecond = 0;
        public int TotalNumberOfBytesProcessed = 0;
        public readonly TimeSpan ConnectionTimeout = new TimeSpan(0, 2, 0);

        public ConcurrentQueue<(IPEndPoint, byte[], string)> EnqueuedDataToSend = new ConcurrentQueue<(IPEndPoint, byte[], string)>();


        public ConcurrentQueue<Dictionary<string,object>> DataProcessInsurance = new ConcurrentQueue<Dictionary<string, object>>();

        public ConcurrentBag<Dictionary<string, object>> BotSpawnData = new ConcurrentBag<Dictionary<string, object>>();
        public ConcurrentBag<Dictionary<string, object>> PlayerSpawnData = new ConcurrentBag<Dictionary<string, object>>();

        public ConcurrentQueue<string> Log = new ConcurrentQueue<string>();

        public readonly ConcurrentDictionary<IPEndPoint, DateTime> PingTimes = new ConcurrentDictionary<IPEndPoint, DateTime>();
        public readonly ConcurrentDictionary<IPEndPoint, DateTime> PongTimes = new ConcurrentDictionary<IPEndPoint, DateTime>();

        public readonly ConcurrentBag<(string, string, long)> ProcessedEvents = new ConcurrentBag<(string, string, long)>();

        public DateTime? GameServerClock { get; private set; }

        public Guid InstanceId  { get; private set; }
        public readonly List<TcpClient> ConnectedClientsTcp = new List<TcpClient>();

        public EchoGameServer(int? startingUdpPort = null)
        {
            if (!Config.EnableCoopServer)
                return;

            InstanceId = Guid.NewGuid();
            if (startingUdpPort.HasValue) udpReceiverPort = startingUdpPort.Value;
            // Only handle 1 instance for now
            //Instances.Clear();
            //Instances.Add(this);
            if (Instance == null)
                Instance = this;
            else
                throw new Exception("Bad instances shithead!");
        }

        public void CreateListenersAndStart()
        {
            if (!Config.EnableCoopServer)
                return;

            //tcpReceiver = new TcpClient();
            udpReceivers = new List<UdpClient>();
            for (var i = 0; i < NumberOfReceivers; i++)
            {
                var newPort = udpReceiverPort + i;
                var udpReceiver = new UdpClient(newPort);
                AddToLog("Started udp receiver on Port " + newPort);
                //udpReceiver.AllowNatTraversal(true);
                //udpReceiver.DontFragment = true;
                //udpReceiver.DontFragment = false;
                udpReceiver.Client.SendTimeout = HighestAcceptablePing;
                udpReceiver.Client.ReceiveTimeout = HighestAcceptablePing;
                const int SIO_UDP_CONNRESET = -1744830452;
                udpReceiver.Client.IOControl(
                    (IOControlCode)SIO_UDP_CONNRESET,
                    new byte[] { 0, 0, 0, 0 },
                    null
                );
                //udpReceiver.Client.ReceiveBufferSize = 50;
                //udpReceiver.Client.SendBufferSize = 300;
                udpReceiver.Client.ReceiveBufferSize = 4096;
                udpReceiver.Client.SendBufferSize = 4096;
              
                udpReceiver.BeginReceive(UdpReceive, udpReceiver);
         
                udpReceivers.Add(udpReceiver);
            }

     
            UpdatePings();
            ServerSendOutEnqueuedData();
        }

        public void UdpReceive(IAsyncResult ar)
        {
            var udpClient = ar.AsyncState as UdpClient;
            try
            {
                IPEndPoint endPoint = null;
                var data = udpClient.EndReceive(ar, ref endPoint);

                while (ProcessingServerData) { }

                ProcessingServerData = true;
                _ = ServerHandleReceivedData(data, endPoint).Result;
                ProcessingServerData = false;
            }
            catch (Exception ex)
            {
                AddToLog(ex.ToString());
            }

            udpClient.BeginReceive(UdpReceive, udpClient);
        }

        public void ResetServer()
        {
            StartupTime = DateTime.Now;

            ConnectedClientToReceiverPort.Clear();
            ConnectedClients.Clear();
            ConnectedClientsLastTimeDataReceiver.Clear();
            PlayersToConnectedClients.Clear();
            MethodCallCounts.Clear();
            DataProcessInsurance.Clear();
            EnqueuedDataToSend.Clear();
            BotSpawnData.Clear();
            PlayerSpawnData.Clear();

            Log = new ConcurrentQueue<string>();

            TotalNumberOfBytesSentLastSecond = 0;
            TotalNumberOfBytesProcessedLastSecond = 0;
            TotalNumberOfBytesProcessed = 0;

            if(OnResetServer != null)
            {
                OnResetServer();
            }
        }

        public async void UpdatePings()
        {
            await Task.Delay(1000);
            var array = Encoding.UTF8.GetBytes("Ping");
            foreach (IPEndPoint item in ConnectedClients.Keys)
            {
                PingTimes.TryRemove(item, out _);
                await udpReceivers[0].SendAsync(array, array.Length, item);
                await Task.Delay(5);
                PingTimes.TryAdd(item, DateTime.Now);
            }
            UpdatePings();
        }


        private async void ServerSendOutEnqueuedData()
        {
            if (DataProcessInsurance.Any())
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(DataProcessInsurance));
                EnqueuedDataToSend.Enqueue((null, bytes, null));
            }
            if (EnqueuedDataToSend.Any())
            {
                var queuedData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(EnqueuedDataToSend.Select(x => Encoding.UTF8.GetString(x.Item2))));
                if (queuedData.Length > 0) 
                { 
                    foreach (IPEndPoint item in ConnectedClients.Keys)
                    {
                        try
                        {
                            //await udpReceivers[0].SendAsync(queuedData, queuedData.Length, item);
                            udpReceivers[0].BeginSend(queuedData, queuedData.Length
                                , item
                                , (IAsyncResult r) => { 

                                }, udpReceivers[0]);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                }
                EnqueuedDataToSend.Clear();
            }
            await Task.Delay(1);
            ServerSendOutEnqueuedData();
        }

        bool ProcessingServerData = false;

        private async Task<bool> ServerHandleReceivedData(byte[] array, IPEndPoint receivedIpEndPoint)
        {
            string @string = Encoding.UTF8.GetString(array);
            if (@string.Length > 0)
            {
                try
                {
                    if (@string.Length == 4 && @string == "Pong")
                    {
                        PongTimes.TryRemove(receivedIpEndPoint, out _);
                        PongTimes.TryAdd(receivedIpEndPoint, DateTime.Now);
                        return true;
                    }

                    // If the "Server" player is saying "Start" then its a new game and clean up!
                    if (@string.StartsWith("Start="))
                    {
                        var accountId = @string.Split("=")[1];
                        PongTimes.TryRemove(receivedIpEndPoint, out _);
                        PongTimes.TryAdd(receivedIpEndPoint, DateTime.Now);
                        PingTimes.TryRemove(receivedIpEndPoint, out _);
                        PingTimes.TryAdd(receivedIpEndPoint, DateTime.Now);
                        ResetServer();
                        AddNewConnection(receivedIpEndPoint, accountId);
                        return true;
                    }

                    if (@string.StartsWith("Connect="))
                    {
                        var accountId = @string.Split("=")[1];

                        PongTimes.TryRemove(receivedIpEndPoint, out _);
                        PongTimes.TryAdd(receivedIpEndPoint, DateTime.Now);
                        PingTimes.TryRemove(receivedIpEndPoint, out _);
                        PingTimes.TryAdd(receivedIpEndPoint, DateTime.Now);
                        AddNewConnection(receivedIpEndPoint, accountId);
                        return true;
                    }

                    if (!ConnectedClientsIPs.ContainsKey(receivedIpEndPoint.ToString()))
                    {
                        AddToLog($"Received data from an unknown connection {receivedIpEndPoint.ToString()}. Ignoring.");
                        return false;
                    }



                    return ServerHandleReceivedJsonData(array, receivedIpEndPoint, @string);


                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }
            }

            return true;
        }

        private bool ServerHandleReceivedJsonData(byte[] array, IPEndPoint receivedIpEndPoint, string @string)
        {
            if (!@string.StartsWith("{") && !@string.EndsWith("}"))
            {
                AddToLog($"Data is not formatted JSON, wtf!");
                AddToLog($"====== Here is the data ======");
                AddToLog($"{@string}");
                return false;
            }

            var dictData = JsonConvert.DeserializeObject<Dictionary<string, object>>(@string);
            if (dictData != null)
            {

                if (!dictData.ContainsKey("method") && !dictData.ContainsKey("m"))
                    return false;

                {


                    if (!dictData.ContainsKey("method"))
                        dictData.Add("method", dictData["m"]);

                    var method = dictData["method"].ToString();
                    if (!string.IsNullOrEmpty(method))
                    {
                        if (!MethodCallCounts.ContainsKey(method))
                        {
                            MethodCallCounts.TryAdd(method, 0);
                        }

                        if (MethodCallCounts.TryGetValue(method, out int callCount))
                        {
                            callCount++;
                            MethodCallCounts[method] = callCount;
                        }

                        if (OnMethodCall != null)
                        {
                            OnMethodCall(MethodCallCounts);
                        }

                        // Batch up Rotations, Positions, Move
                        //if (method == "Rotation" || method == "Position" || method == "Move")
                        //{
                        //    EnqueuedDataToSend.Enqueue((null, array, null));
                        //    return true;
                        //}

                        if (method == "PlayerSpawn")
                        {
                            if (PlayerSpawnData.Count > 0) // if client, tell these people about server person
                            {
                                var firstPlayer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(PlayerSpawnData.First()));
                                udpReceivers[0].BeginSend(firstPlayer, firstPlayer.Length, receivedIpEndPoint, (IAsyncResult r) => {
                                }, udpReceivers[0]);
                            }

                            PlayerSpawnData.Add(dictData);
                        }

                        foreach (var client in PlayersToConnectedClients)
                        {
                            foreach (var udpServer in udpReceivers)
                            {
                                udpServer.BeginSend(array, array.Length, client.Value, (IAsyncResult r) => {
                                }, udpServer);
                            }
                        }

                        // Always push Dead calls !
                        if (method == "Dead" || method == "Damage")
                        {
                            DataProcessInsurance.Enqueue(dictData);
                        }
                    }

                    //string accountId = dictData["accountId"].ToString();

                    //if (dictData.ContainsKey("tick"))
                    //{
                    //    long ticks = long.Parse(dictData["tick"].ToString());
                    //    if (!ProcessedEvents.Any(x => x.Item1 == method && x.Item2 == accountId && x.Item3 == ticks))
                    //        EnqueuedDataToSend.Enqueue((receivedIpEndPoint, array, accountId));

                    //    ProcessedEvents.Add((method, accountId, ticks));
                    //}
                    //else
                    //{
                    //}
                    //foreach (var client in ConnectedClients.Keys)
                    //{
                    //    udpReceivers[0].Send(array, array.Length, client);
                    //    udpReceivers[1].Send(array, array.Length, client);
                    //}


                }
            }

            return false;
        }

        /// <summary>
        /// Send parity data to ALL clients
        /// </summary>
        //private async void SendParityData()
        //{
        //    AddToLog($"Sending out Spawn Data, {PlayerSpawnData.Count} Players, {BotSpawnData.Count} Bots");
        //    foreach (var bpd in BotSpawnData)
        //    {
        //        EnqueuedDataToSend.Enqueue((null, Encoding.Default.GetBytes(JsonConvert.SerializeObject(bpd))));
        //        await Task.Delay(1000); // Delay by 1 second to ensure data is actually sent
        //    }
        //    foreach (var bpd in PlayerSpawnData)
        //    {
        //        EnqueuedDataToSend.Enqueue((null, Encoding.Default.GetBytes(JsonConvert.SerializeObject(bpd))));
        //        await Task.Delay(1000); // Delay by 1 second to ensure data is actually sent
        //    }
        //}

        bool addingNewConnection = false;

        private async void AddNewConnection(IPEndPoint receivedIpEndPoint, string playerId, bool isHost = false)
        {
            if (receivedIpEndPoint == null)
            {
                Console.WriteLine("IP End Point is NULL! WTF!");
                return;
            }

            while (addingNewConnection) { await Task.Delay(1000); }

            if (udpReceivers.Count == 0)
                return;

            addingNewConnection = true;

            if (ConnectedClientsIPs.TryAdd(receivedIpEndPoint.ToString(), playerId))
            {
                //if (!ConnectedClients.Keys.Any((IPEndPoint x) => x.ToString() == receivedIpEndPoint.ToString()))
                //{
                    // continuously attempt to add
                if (ConnectedClients.TryAdd(receivedIpEndPoint, playerId))
                {
                    NumberOfConnections++;

                    if (ConnectedClientToReceiverPort.TryAdd(receivedIpEndPoint, (udpReceivers[0], udpReceiverPort)))
                    {

                        if (isHost)
                            HostConnection = (receivedIpEndPoint, playerId);

                        Debug.WriteLine("DataReceivedServer::New Connection from " + receivedIpEndPoint.ToString());
                        Console.WriteLine("DataReceivedServer::New Connection from " + receivedIpEndPoint.ToString());
                        AddToLog("DataReceivedServer::New Connection from " + receivedIpEndPoint.ToString());
                        if (OnConnectionReceived != null)
                        {
                            OnConnectionReceived(receivedIpEndPoint);
                        }
                        var connectedMessage = "Connected=" + playerId;
                        var connectedMessageArray = UTF8Encoding.UTF8.GetBytes(connectedMessage);
                        PlayersToConnectedClients.TryAdd(playerId, receivedIpEndPoint);
                        udpReceivers[0].Send(connectedMessageArray, connectedMessageArray.Length, receivedIpEndPoint);

                    }
                }
                //}
            }

            addingNewConnection = false;
        }

        public void AddToLog(string text)
        {
            //Debug.WriteLine(text);
            if(OnLog != null)
            {
                OnLog(text);
            }
        }

    }

}
