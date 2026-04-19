using Lidgren.Network;
using LmpClient.Systems.SettingsSys;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using LmpCommon.RepoRetrievers;
using LmpCommon.Time;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;

namespace LmpClient.Network
{
    public class NetworkSender
    {
        public static ConcurrentQueue<IMessageBase> OutgoingMessages { get; set; } = new ConcurrentQueue<IMessageBase>();
        private const int MaxMessagesPerBatch = 128;

        /// <summary>
        /// Cap on the outgoing queue size. If the send thread falls behind (CPU pressure, network hiccup),
        /// we drop the oldest messages rather than let the queue grow without bound. At 60 messages/sec
        /// this is ~1 minute of buffered traffic - generous for legitimate lag, but catches pathological growth.
        /// </summary>
        private const int MaxOutgoingQueueSize = 4096;

        /// <summary>
        /// Diagnostic counter: how many messages we've dropped because the queue exceeded its cap. Exposed for
        /// statistics/debug windows; not an authoritative metric but useful in incident investigation.
        /// </summary>
        public static long DroppedOverflowMessages;

        /// <summary>
        /// Main sending thread
        /// </summary>
        public static void SendMain()
        {
            LunaLog.Log("[LMP]: Send thread started");
            try
            {
                while (!NetworkConnection.ResetRequested)
                {
                    var sentAnyMessages = false;
                    var sentMessages = 0;

                    while (sentMessages < MaxMessagesPerBatch && OutgoingMessages.TryDequeue(out var sendMessage))
                    {
                        sentAnyMessages |= SendNetworkMessage(sendMessage);
                        sentMessages++;
                    }

                    if (sentAnyMessages)
                    {
                        NetworkMain.ClientConnection?.FlushSendQueue();
                    }
                    else
                    {
                        Thread.Sleep(SettingsSystem.CurrentSettings.SendReceiveMsInterval);
                    }
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Send thread error: {e}");
            }
            LunaLog.Log("[LMP]: Send thread exited");
        }

        /// <summary>
        /// Adds a new message to the queue. If the queue is over capacity, drop the oldest entry (FIFO) and
        /// increment <see cref="DroppedOverflowMessages"/>. Dropping oldest (not newest) preserves freshness
        /// of gameplay-relevant state while still keeping the queue bounded.
        /// </summary>
        public static void QueueOutgoingMessage(IMessageBase message)
        {
            if (message == null) return;

            OutgoingMessages.Enqueue(message);

            while (OutgoingMessages.Count > MaxOutgoingQueueSize && OutgoingMessages.TryDequeue(out var dropped))
            {
                Interlocked.Increment(ref DroppedOverflowMessages);
                dropped?.Recycle();
            }
        }

        /// <summary>
        /// Sends the network message. It will skip client messages to send when we are not connected,
        /// except if it's directed at master servers, then it will start the NetClient and socket.
        /// </summary>
        private static bool SendNetworkMessage(IMessageBase message)
        {
            message.Data.SentTime = LunaNetworkTime.UtcNow.Ticks;
            try
            {
                if (message is IMasterServerMessageBase)
                {
                    if (NetworkMain.ClientConnection.Status == NetPeerStatus.NotRunning)
                    {
                        LunaLog.Log("Starting client to send unconnected message");
                        NetworkMain.ClientConnection.Start();
                    }
                    while (NetworkMain.ClientConnection.Status != NetPeerStatus.Running)
                    {
                        LunaLog.Log("Waiting for client to start up to send unconnected message");
                        // Still trying to start up
                        Thread.Sleep(50);
                    }

                    IPEndPoint[] masterServers;
                    if (string.IsNullOrEmpty(SettingsSystem.CurrentSettings.CustomMasterServer))
                        masterServers = MasterServerRetriever.MasterServers.GetValues;
                    else
                    {
                        masterServers = new[]
                        {
                            LunaNetUtils.CreateEndpointFromString(SettingsSystem.CurrentSettings.CustomMasterServer)
                        };

                    }
                    foreach (var masterServer in masterServers)
                    {
                        // Don't reuse lidgren messages, it does that on it's own
                        var lidgrenMsg = NetworkMain.ClientConnection.CreateMessage(message.GetMessageSize());

                        message.Serialize(lidgrenMsg);
                        NetworkMain.ClientConnection.SendUnconnectedMessage(lidgrenMsg, masterServer);
                    }
                    message.Recycle();
                    return true;
                }

                if (NetworkMain.ClientConnection == null || NetworkMain.ClientConnection.Status == NetPeerStatus.NotRunning
                    || MainSystem.NetworkState < ClientState.Connected)
                {
                    return false;
                }

                var outgoingMessage = NetworkMain.ClientConnection.CreateMessage(message.GetMessageSize());

                message.Serialize(outgoingMessage);
                NetworkMain.ClientConnection.SendMessage(outgoingMessage, message.NetDeliveryMethod, message.Channel);

                message.Recycle();
                return true;
            }
            catch (Exception e)
            {
                NetworkMain.HandleDisconnectException(e);
                return false;
            }
        }
    }
}
