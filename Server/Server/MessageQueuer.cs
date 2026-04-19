using Lidgren.Network;
using LmpCommon.Message.Interface;
using Server.Client;
using Server.Context;
using System;
using System.Collections.Generic;

namespace Server.Server
{
    public class MessageQueuer
    {
        /// <summary>
        /// Sends a message to all the clients except the one given as parameter that are in the same subspace
        /// </summary>
        public static void RelayMessageToSubspace<T>(ClientStructure exceptClient, IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            RelayMessageToSubspace<T>(exceptClient, data, exceptClient.Subspace);
        }

        /// <summary>
        /// Sends a message to all the clients in the given subspace
        /// </summary>
        public static void SendMessageToSubspace<T>(IMessageData data, int subspace) where T : class, IServerMessageBase
        {
            if (data == null) return;

            Broadcast<T>(data, client => client.Subspace == subspace);
        }

        /// <summary>
        /// Sends a message to all the clients except the one given as parameter that are in the subspace given as parameter
        /// </summary>
        public static void RelayMessageToSubspace<T>(ClientStructure exceptClient, IMessageData data, int subspace) where T : class, IServerMessageBase
        {
            if (data == null) return;

            Broadcast<T>(data, client => !Equals(client, exceptClient) && client.Subspace == subspace);
        }

        /// <summary>
        /// Sends a message to all the clients except the one given as parameter
        /// </summary>
        public static void RelayMessage<T>(ClientStructure exceptClient, IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            Broadcast<T>(data, client => !Equals(client, exceptClient));
        }

        /// <summary>
        /// Sends a message to all the clients
        /// </summary>
        public static void SendToAllClients<T>(IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            Broadcast<T>(data, _ => true);
        }

        /// <summary>
        /// Sends a message to the given client
        /// </summary>
        public static void SendToClient<T>(ClientStructure client, IMessageData data) where T : class, IServerMessageBase
        {
            if (data == null) return;

            SendToClient(client, GenerateMessage<T>(data));
        }

        /// <summary>
        /// Disconnects the given client
        /// </summary>
        public static void SendConnectionEnd(ClientStructure client, string reason)
        {
            ClientConnectionHandler.DisconnectClient(client, reason);
        }

        /// <summary>
        /// Disconnect all clients
        /// </summary>
        public static void SendConnectionEndToAll(string reason)
        {
            foreach (var client in ClientRetriever.GetAuthenticatedClients())
                SendConnectionEnd(client, reason);
        }

        #region Private

        private static void SendToClient(ClientStructure client, IServerMessageBase msg)
        {
            if (msg?.Data == null) return;

            // EnqueueMessage applies a bounded queue (drop-oldest on overflow) so a stuck client cannot cause
            // per-client queue growth on the server.
            client?.EnqueueMessage(msg);
        }

        private static T GenerateMessage<T>(IMessageData data) where T : class, IServerMessageBase
        {
            var newMessage = ServerContext.ServerMessageFactory.CreateNew<T>(data);
            return newMessage;
        }

        /// <summary>
        /// Serialize once, Lidgren-broadcasts to all connections matching <paramref name="predicate"/>, and then
        /// fully recycles the wrapper + shared <see cref="IMessageData"/>. This avoids the N-way concurrent
        /// serialize / compress on a single shared Data instance that the old per-client-enqueue path caused.
        /// </summary>
        private static void Broadcast<T>(IMessageData data, Func<ClientStructure, bool> predicate) where T : class, IServerMessageBase
        {
            var recipients = new List<NetConnection>(ServerContext.Clients.Count);
            foreach (var client in ServerContext.Clients.Values)
            {
                if (client?.Connection == null) continue;
                if (predicate(client)) recipients.Add(client.Connection);
            }

            if (recipients.Count == 0)
            {
                // Nothing to send - but we still need to recycle the Data (otherwise pooled IMessageData leaks).
                // Wrap and recycle through the normal path.
                var empty = GenerateMessage<T>(data);
                empty?.Recycle();
                return;
            }

            var msg = GenerateMessage<T>(data);
            try
            {
                LidgrenServer.BroadcastMessage(recipients, msg);
                // AutoFlushSendQueue is disabled on the server config; broadcasts go direct, so flush here
                // to prevent broadcasts from being stuck until the next per-client send-thread tick.
                LidgrenServer.FlushSendQueue();
            }
            finally
            {
                // Safe: Lidgren has serialized into its own outgoing buffer already, so the shared Data
                // is no longer needed by the send path.
                msg.Recycle();
            }
        }

        #endregion
    }
}