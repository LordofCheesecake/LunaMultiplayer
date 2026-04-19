using Lidgren.Network;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using Server.Context;
using Server.Plugin;
using Server.Server;
using Server.Settings.Structures;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Client
{
    public class ClientStructure
    {
        public IPEndPoint Endpoint => Connection.RemoteEndPoint;

        public string UniqueIdentifier { get; set; }
        public string KspVersion { get; set; }
        public string LmpVersion { get; set; }

        public bool Authenticated { get; set; }

        public long BytesReceived { get; set; }
        public long BytesSent { get; set; }
        public NetConnection Connection { get; }

        public ConnectionStatus ConnectionStatus { get; set; } = ConnectionStatus.Connected;
        public bool DisconnectClient { get; set; }
        public long LastReceiveTime { get; set; } = ServerContext.ServerClock.ElapsedMilliseconds;
        public long LastSendTime { get; set; } = 0;
        public float[] PlayerColor { get; set; } = new float[3];
        public string PlayerName { get; set; } = "Unknown";
        public PlayerStatus PlayerStatus { get; set; } = new PlayerStatus();

        /// <summary>
        /// Per-client send queue for point-to-point messages. Broadcast traffic now bypasses this via
        /// <see cref="LidgrenServer.BroadcastMessage"/>, so this queue typically stays small. A hard cap is
        /// enforced in <see cref="EnqueueMessage"/> so a stuck send thread cannot grow this unboundedly.
        /// </summary>
        public ConcurrentQueue<IServerMessageBase> SendMessageQueue { get; } = new ConcurrentQueue<IServerMessageBase>();

        /// <summary>
        /// Upper bound on the per-client send queue size. Drop-oldest policy beyond this. Tune if legitimate
        /// bursty broadcasts (e.g. initial sync) exceed this for brief windows.
        /// </summary>
        public const int MaxSendQueueSize = 2048;

        /// <summary>
        /// Incremented for each message dropped due to queue overflow; useful for incident diagnostics.
        /// </summary>
        public long DroppedOverflowMessages;

        public int Subspace { get; set; } = int.MinValue; //Leave it as min value. When client connect we force them client side to go to latest subspace
        public float SubspaceRate { get; set; } = 1f;

        /// <summary>
        /// Enqueue a message for this client with drop-oldest backpressure when the queue is saturated.
        /// </summary>
        public void EnqueueMessage(IServerMessageBase message)
        {
            if (message?.Data == null) return;

            SendMessageQueue.Enqueue(message);

            while (SendMessageQueue.Count > MaxSendQueueSize && SendMessageQueue.TryDequeue(out var dropped))
            {
                Interlocked.Increment(ref DroppedOverflowMessages);
                dropped?.RecycleWrapperOnly();
            }
        }

        public DateTime ConnectionTime { get; } = DateTime.UtcNow;

        public Task SendThread { get; }

        /// <summary>
        /// Per-client inbound queue. The Lidgren receive thread deserializes incoming messages and enqueues them
        /// here so the Lidgren thread is not blocked by per-client handler work (lock acquisition, logging,
        /// pre-Task.Run logic). Messages for one client still run in the order received; messages across
        /// different clients can be processed in parallel.
        /// </summary>
        public ConcurrentQueue<IClientMessageBase> ReceiveMessageQueue { get; } = new ConcurrentQueue<IClientMessageBase>();

        /// <summary>
        /// Cap on the inbound queue size. Beyond this we drop the oldest queued message and increment
        /// <see cref="DroppedOverflowReceiveMessages"/>. Keeps a slow handler from ballooning server memory.
        /// </summary>
        public const int MaxReceiveQueueSize = 2048;

        public long DroppedOverflowReceiveMessages;

        public Task ReceiveThread { get; }

        public ClientStructure(NetConnection playerConnection)
        {
            Connection = playerConnection;
            SendThread = MainServer.LongRunTaskFactory.StartNew(() => SendMessagesThreadAsync(MainServer.CancellationTokenSrc.Token), MainServer.CancellationTokenSrc.Token);
            ReceiveThread = MainServer.LongRunTaskFactory.StartNew(() => ProcessReceivedMessagesAsync(MainServer.CancellationTokenSrc.Token), MainServer.CancellationTokenSrc.Token);
        }

        /// <summary>
        /// Enqueue a deserialized inbound message for this client. Drop-oldest policy on overflow.
        /// </summary>
        public void EnqueueReceivedMessage(IClientMessageBase message)
        {
            if (message?.Data == null) return;

            ReceiveMessageQueue.Enqueue(message);

            while (ReceiveMessageQueue.Count > MaxReceiveQueueSize && ReceiveMessageQueue.TryDequeue(out var dropped))
            {
                Interlocked.Increment(ref DroppedOverflowReceiveMessages);
                dropped?.Recycle();
            }
        }

        private async Task ProcessReceivedMessagesAsync(CancellationToken token)
        {
            while (ConnectionStatus == ConnectionStatus.Connected && !token.IsCancellationRequested)
            {
                var processed = 0;
                while (processed < MaxMessagesPerBatch && ReceiveMessageQueue.TryDequeue(out var message) && message != null)
                {
                    LidgrenServer.ClientMessageReceiver.DispatchDeserializedMessage(this, message);
                    processed++;
                }

                if (processed == 0)
                {
                    try
                    {
                        await Task.Delay(IntervalSettings.SettingsStore.SendReceiveThreadTickMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Count of consecutive handler exceptions observed for this client. Reset on any successful handle.
        /// If it crosses <see cref="PoisonMessageDisconnectThreshold"/> the client is dropped; protects the
        /// server against a buggy/malicious client producing infinite error log spam.
        /// </summary>
        public int ConsecutiveHandlerFailures { get; set; }

        public const int PoisonMessageDisconnectThreshold = 5;

        /// <summary>
        /// Simple token-bucket rate limit on inbound messages. Budget regenerates at
        /// <see cref="InboundTokenRefillPerSecond"/> tokens/sec up to <see cref="InboundTokenBucketMax"/>.
        /// When the bucket is empty, the message is dropped (and counted in <see cref="RateLimitedMessages"/>).
        /// This caps the work any single client can inflict on the server's receive thread.
        /// </summary>
        public const int InboundTokenBucketMax = 1024;

        public const int InboundTokenRefillPerSecond = 256;

        private double _inboundTokens = InboundTokenBucketMax;

        private long _lastTokenRefillMs;

        public long RateLimitedMessages;

        /// <summary>
        /// Returns true if the client may send another inbound message right now (decrements the token).
        /// Thread-safe via a lock on <c>this</c>.
        /// </summary>
        public bool TryConsumeInboundToken()
        {
            lock (SendMessageQueue) // reuse existing reference as a lock target; no value semantics used.
            {
                var nowMs = ServerContext.ServerClock.ElapsedMilliseconds;
                var elapsedMs = nowMs - _lastTokenRefillMs;
                if (elapsedMs > 0)
                {
                    _inboundTokens = Math.Min(InboundTokenBucketMax, _inboundTokens + elapsedMs * InboundTokenRefillPerSecond / 1000d);
                    _lastTokenRefillMs = nowMs;
                }

                if (_inboundTokens < 1d)
                {
                    Interlocked.Increment(ref RateLimitedMessages);
                    return false;
                }

                _inboundTokens -= 1d;
                return true;
            }
        }

        public override bool Equals(object obj)
        {
            var clientToCompare = obj as ClientStructure;
            return Endpoint.Equals(clientToCompare?.Endpoint);
        }

        public override int GetHashCode()
        {
            return Endpoint?.GetHashCode() ?? 0;
        }

        private const int MaxMessagesPerBatch = 128;

        private async Task SendMessagesThreadAsync(CancellationToken token)
        {
            while (ConnectionStatus == ConnectionStatus.Connected)
            {
                var sentCount = 0;
                while (sentCount < MaxMessagesPerBatch && SendMessageQueue.TryDequeue(out var message) && message != null)
                {
                    try
                    {
                        LidgrenServer.SendMessageToClient(this, message);
                        sentCount++;
                    }
                    catch (Exception e)
                    {
                        ClientException.HandleDisconnectException("Send network message error: ", this, e);
                        return;
                    }

                    LmpPluginHandler.FireOnMessageSent(this, message);

                    // After send: the Lidgren NetOutgoingMessage already owns its byte buffer, so the
                    // IServerMessageBase wrapper can go back to the pool. The underlying IMessageData is often
                    // shared across per-client broadcast wrappers, so only recycle the wrapper here.
                    message.RecycleWrapperOnly();
                }

                if (sentCount > 0)
                {
                    LidgrenServer.FlushSendQueue();
                    continue;
                }

                try
                {
                    await Task.Delay(IntervalSettings.SettingsStore.SendReceiveThreadTickMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
