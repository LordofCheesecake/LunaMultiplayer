using ByteSizeLib;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Server;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Message.Base;
using Server.Server;
using Server.System;
using Server.System.Vessel;
using System;
using System.Linq;
using System.Text;

namespace Server.Message
{
    public class VesselMsgReader : ReaderBase
    {
        /// <summary>
        /// Returns true if the sender is allowed to mutate the given vessel's Update-lock-scoped state.
        /// If no Update lock has been acquired for the vessel yet (e.g. initial proto, or nobody currently
        /// owns the vessel), we permit the message - otherwise the sender must own the Update lock.
        /// Same gating applies to position/update/resource/part-sync/actiongroup/fairing messages which are
        /// all semantically "the Update-lock owner is driving this vessel".
        /// </summary>
        private static bool SenderMayMutateVessel(ClientStructure client, Guid vesselId)
        {
            if (!LockSystem.LockQuery.UpdateLockExists(vesselId))
                return true;
            return LockSystem.LockQuery.UpdateLockBelongsToPlayer(vesselId, client.PlayerName);
        }

        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var messageData = message.Data as VesselBaseMsgData;
            if (messageData == null)
            {
                // message.Data being null here means either the wrapper was served from the pool with a cleared
                // payload (pool-state race — see central dispatch finally) or the incoming subtype could not be
                // resolved to VesselBaseMsgData. Log with wrapper identity so we can tell the two apart.
                LunaLog.Warning($"Vessel message from {client.PlayerName} had no VesselBaseMsgData payload " +
                                $"(Data={(message.Data?.GetType().Name ?? "<null>")}, wrapperHash={message.GetHashCode():X}).");
                return;
            }

            switch (messageData.VesselMessageType)
            {
                case VesselMessageType.Sync:
                    HandleVesselsSync(client, messageData);
                    break;
                case VesselMessageType.Proto:
                    HandleVesselProto(client, messageData);
                    break;
                case VesselMessageType.Remove:
                    HandleVesselRemove(client, messageData);
                    break;
                case VesselMessageType.Position:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    // LatestSubspace can be null during shutdown/reset; only persist when on the authoritative subspace.
                    var latestSubspaceForPos = WarpContext.LatestSubspace;
                    if (latestSubspaceForPos != null && client.Subspace == latestSubspaceForPos.Id)
                        VesselDataUpdater.WritePositionDataToFile(messageData);
                    break;
                case VesselMessageType.Flightstate:
                    // Flight state is the Control-lock owner's per-frame ctrl input; gate on Control lock.
                    if (LockSystem.LockQuery.ControlLockExists(messageData.VesselId) &&
                        !LockSystem.LockQuery.ControlLockBelongsToPlayer(messageData.VesselId, client.PlayerName))
                        return;
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    VesselDataUpdater.WriteFlightstateDataToFile(messageData);
                    break;
                case VesselMessageType.Update:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    VesselDataUpdater.WriteUpdateDataToFile(messageData);
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.Resource:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    VesselDataUpdater.WriteResourceDataToFile(messageData);
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.PartSyncField:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    VesselDataUpdater.WritePartSyncFieldDataToFile(messageData);
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.PartSyncUiField:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    VesselDataUpdater.WritePartSyncUiFieldDataToFile(messageData);
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.PartSyncCall:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.ActionGroup:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    VesselDataUpdater.WriteActionGroupDataToFile(messageData);
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.Fairing:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    VesselDataUpdater.WriteFairingDataToFile(messageData);
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.Decouple:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                case VesselMessageType.Couple:
                    HandleVesselCouple(client, messageData);
                    break;
                case VesselMessageType.Undock:
                    if (!SenderMayMutateVessel(client, messageData.VesselId)) return;
                    MessageQueuer.RelayMessage<VesselSrvMsg>(client, messageData);
                    break;
                default:
                    LunaLog.Debug($"Ignoring vessel message subtype {messageData.VesselMessageType} from {client.PlayerName}");
                    break;
            }
        }

        private static void HandleVesselRemove(ClientStructure client, VesselBaseMsgData message)
        {
            var data = (VesselRemoveMsgData)message;

            if (LockSystem.LockQuery.ControlLockExists(data.VesselId) && !LockSystem.LockQuery.ControlLockBelongsToPlayer(data.VesselId, client.PlayerName))
                return;

            if (VesselStoreSystem.VesselExists(data.VesselId))
            {
                LunaLog.Debug($"Removing vessel {data.VesselId} from {client.PlayerName}");
                VesselStoreSystem.RemoveVessel(data.VesselId);
            }

            if (data.AddToKillList)
                VesselContext.AddRemovedVessel(data.VesselId);

            //Relay the message.
            MessageQueuer.RelayMessage<VesselSrvMsg>(client, data);
        }

        private static void HandleVesselProto(ClientStructure client, VesselBaseMsgData message)
        {
            var msgData = (VesselProtoMsgData)message;

            if (VesselContext.RemovedVessels.ContainsKey(msgData.VesselId)) return;

            if (msgData.NumBytes == 0)
            {
                LunaLog.Warning($"Received a vessel with 0 bytes ({msgData.VesselId}) from {client.PlayerName}.");
                return;
            }

            if (!VesselStoreSystem.VesselExists(msgData.VesselId))
            {
                LunaLog.Debug($"Saving vessel {msgData.VesselId} ({ByteSize.FromBytes(msgData.NumBytes).KiloBytes} KB) from {client.PlayerName}.");
            }

            var accepted = VesselDataUpdater.RawConfigNodeInsertOrUpdate(msgData.VesselId, msgData.GameTime, Encoding.UTF8.GetString(msgData.Data, 0, msgData.NumBytes));
            if (!accepted)
            {
                // Drop relay too: forwarding a strictly-older snapshot would force other clients into the same revert.
                return;
            }

            MessageQueuer.RelayMessage<VesselSrvMsg>(client, msgData);
        }

        private static void HandleVesselsSync(ClientStructure client, VesselBaseMsgData message)
        {
            var msgData = (VesselSyncMsgData)message;

            var allVessels = VesselStoreSystem.CurrentVessels.Keys.ToList();

            //Here we only remove the vessels that the client ALREADY HAS so we only send the vessels they DON'T have
            for (var i = 0; i < msgData.VesselsCount; i++)
                allVessels.Remove(msgData.VesselIds[i]);

            var vesselsToSend = allVessels;
            foreach (var vesselId in vesselsToSend)
            {
                var vesselData = VesselStoreSystem.GetVesselInConfigNodeFormat(vesselId);
                // Vessel may have been concurrently removed between Keys snapshot and serialization - skip those.
                if (!string.IsNullOrEmpty(vesselData))
                {
                    var protoMsg = ServerContext.ServerMessageFactory.CreateNewMessageData<VesselProtoMsgData>();
                    protoMsg.Data = Encoding.UTF8.GetBytes(vesselData);
                    protoMsg.NumBytes = vesselData.Length;
                    protoMsg.VesselId = vesselId;

                    MessageQueuer.SendToClient<VesselSrvMsg>(client, protoMsg);
                }
            }

            if (allVessels.Count > 0)
                LunaLog.Debug($"Sending {client.PlayerName} {vesselsToSend.Count} vessels");
        }

        private static void HandleVesselCouple(ClientStructure client, VesselBaseMsgData message)
        {
            var msgData = (VesselCoupleMsgData)message;

            LunaLog.Debug($"Coupling message received! Dominant vessel: {msgData.VesselId}");
            MessageQueuer.RelayMessage<VesselSrvMsg>(client, msgData);

            if (VesselContext.RemovedVessels.ContainsKey(msgData.CoupledVesselId)) return;

            //Now remove the weak vessel but DO NOT add to the removed vessels as they might undock!!!
            LunaLog.Debug($"Removing weak coupled vessel {msgData.CoupledVesselId}");
            VesselStoreSystem.RemoveVessel(msgData.CoupledVesselId);

            //Tell all clients to remove the weak vessel
            var removeMsgData = ServerContext.ServerMessageFactory.CreateNewMessageData<VesselRemoveMsgData>();
            removeMsgData.VesselId = msgData.CoupledVesselId;

            MessageQueuer.SendToAllClients<VesselSrvMsg>(removeMsgData);
        }
    }
}
