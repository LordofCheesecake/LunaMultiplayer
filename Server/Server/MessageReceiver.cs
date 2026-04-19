using Lidgren.Network;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using LmpCommon.Time;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Message;
using Server.Message.Base;
using Server.Plugin;
using System;
using System.Collections.Generic;

namespace Server.Server
{
    public class MessageReceiver
    {
        #region Handlers

        private static readonly Dictionary<ClientMessageType, ReaderBase> HandlerDictionary = new Dictionary
            <ClientMessageType, ReaderBase>
        {
            [ClientMessageType.Admin] = new AdminMsgReader(),
            [ClientMessageType.Handshake] = new HandshakeMsgReader(),
            [ClientMessageType.Chat] = new ChatMsgReader(),
            [ClientMessageType.PlayerStatus] = new PlayerStatusMsgReader(),
            [ClientMessageType.PlayerColor] = new PlayerColorMsgReader(),
            [ClientMessageType.Scenario] = new ScenarioDataMsgReader(),
            [ClientMessageType.Kerbal] = new KerbalMsgReader(),
            [ClientMessageType.Settings] = new SettingsMsgReader(),
            [ClientMessageType.Vessel] = new VesselMsgReader(),
            [ClientMessageType.CraftLibrary] = new CraftLibraryMsgReader(),
            [ClientMessageType.Flag] = new FlagSyncMsgReader(),
            [ClientMessageType.Motd] = new MotdMsgReader(),
            [ClientMessageType.Warp] = new WarpControlMsgReader(),
            [ClientMessageType.Lock] = new LockSystemMsgReader(),
            [ClientMessageType.Mod] = new ModDataMsgReader(),
            [ClientMessageType.Groups] = new GroupMsgReader(),
            [ClientMessageType.Facility] = new FacilityMsgReader(),
            [ClientMessageType.Screenshot] = new ScreenshotMsgReader(),
            [ClientMessageType.ShareProgress] = new ShareProgressMsgReader(),
        };

        #endregion

        /// <summary>
        /// Called on the Lidgren receive thread. Deserializes the raw inbound bytes into a pooled
        /// <see cref="IClientMessageBase"/> and hands off to the client's per-client receive worker via
        /// <see cref="ClientStructure.EnqueueReceivedMessage"/>. Keeping the Lidgren thread lean avoids head-of-line
        /// blocking across unrelated clients when a single client's handler path is slow.
        /// </summary>
        public void ReceiveCallback(ClientStructure client, NetIncomingMessage msg)
        {
            if (client == null || msg.LengthBytes <= 1) return;

            if (client.ConnectionStatus == ConnectionStatus.Connected)
                client.LastReceiveTime = ServerContext.ServerClock.ElapsedMilliseconds;

            // Per-client rate limit: stops a misbehaving client from flooding the receive thread.
            if (!client.TryConsumeInboundToken())
            {
                // Drop silently; counter (RateLimitedMessages) is visible for diagnostics.
                return;
            }

            if (!TryDeserializeInboundMessage(msg, client, out var message))
                return;

            client.ConsecutiveHandlerFailures = 0;
            client.EnqueueReceivedMessage(message);
        }

        /// <summary>
        /// Runs on the client's receive worker task. Performs plugin fire, version/auth checks, then dispatches
        /// to the appropriate <see cref="ReaderBase"/> handler. Wrapper-only recycle in the finally; see comment
        /// in <see cref="MessageReceiver"/> about shared <see cref="LmpCommon.Message.Interface.IMessageData"/>.
        /// </summary>
        public void DispatchDeserializedMessage(ClientStructure client, IClientMessageBase message)
        {
            if (client == null || message == null) return;

            try
            {
                LmpPluginHandler.FireOnMessageReceived(client, message);
                //A plugin has handled this message and requested suppression of the default behavior
                if (message.Handled) return;

                if (message.VersionMismatch)
                {
                    MessageQueuer.SendConnectionEnd(client, $"Version mismatch: Your version ({message.Data.MajorVersion}.{message.Data.MinorVersion}.{message.Data.BuildVersion}) " +
                                                            $"does not match the server version: {LmpVersioning.CurrentVersion}.");
                    return;
                }

                //Clients can only send HANDSHAKE until they are Authenticated.
                if (!client.Authenticated && message.MessageType != ClientMessageType.Handshake)
                {
                    MessageQueuer.SendConnectionEnd(client, $"You must authenticate before sending a {message.MessageType} message");
                    return;
                }

                //Handle the message (handler bugs are logged but do not contribute to deserialization poison counter).
                try
                {
                    HandlerDictionary[message.MessageType].HandleMessage(client, message);
                }
                catch (Exception e)
                {
                    LunaLog.Error($"Error handling a {message.MessageType} message from {client.PlayerName}! {e}");
                }
            }
            finally
            {
                message.RecycleWrapperOnly();
            }
        }

        /// <summary>
        /// Deserializes an inbound client message. On failure, increments
        /// <see cref="ClientStructure.ConsecutiveHandlerFailures"/> (deserialization poison counter) and may disconnect.
        /// </summary>
        private static bool TryDeserializeInboundMessage(NetIncomingMessage msg, ClientStructure client, out IClientMessageBase message)
        {
            message = null;
            try
            {
                var deserialized = ServerContext.ClientMessageFactory.Deserialize(msg, LunaNetworkTime.UtcNow.Ticks);
                message = deserialized as IClientMessageBase;
                if (message == null)
                {
                    deserialized?.Recycle();
                    LunaLog.Error("Deserialized inbound payload is not a client message type");
                    ReportInboundDeserializationPoison(client);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                LunaLog.Error($"Error deserializing message! {e}");
                ReportInboundDeserializationPoison(client);
                return false;
            }
        }

        private static void ReportInboundDeserializationPoison(ClientStructure client)
        {
            if (client == null)
                return;

            client.ConsecutiveHandlerFailures++;
            if (client.ConsecutiveHandlerFailures < ClientStructure.PoisonMessageDisconnectThreshold)
                return;

            LunaLog.Warning($"Disconnecting {client.PlayerName}: exceeded poison-message threshold ({client.ConsecutiveHandlerFailures} consecutive deserialization failures)");
            MessageQueuer.SendConnectionEnd(client, "Too many malformed messages");
        }
    }
}
