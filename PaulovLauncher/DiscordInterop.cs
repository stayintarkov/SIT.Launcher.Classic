using NetDiscordRpc;
using NetDiscordRpc.RPC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Launcher
{
    public class DiscordInterop
    {
        public static DiscordRPC DiscordRpcClient;

        public async Task<DiscordRPC> StartDiscordClient(string productVersion = "")
        {
            return await Task.Run(() =>
            {
                if (DiscordRpcClient == null)
                {
                    DiscordRpcClient = new DiscordRPC("983769140684791808");
                    DiscordRpcClient.Logger = new NetDiscordRpc.Core.Logger.NullLogger();
                    DiscordRpcClient.Initialize();
                    DiscordRpcClient.SetPresence(new RichPresence()
                    {
                        Details = "Main Menu",
                        State = productVersion,
                    });
                    DiscordRpcClient.Invoke();

                }
                return DiscordRpcClient;
            });
        }

        public DiscordRPC GetDiscordRPC()
        {
            return StartDiscordClient().Result;
        }
    }
}
