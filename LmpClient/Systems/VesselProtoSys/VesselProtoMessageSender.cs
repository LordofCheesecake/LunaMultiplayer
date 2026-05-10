using LmpClient;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Network;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Utilities;
using LmpClient.VesselUtilities;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Interface;
using System;

namespace LmpClient.Systems.VesselProtoSys
{
    public class VesselProtoMessageSender : SubSystem<VesselProtoSystem>, IMessageSender
    {
        /// <summary>
        /// Pre allocated array to store the vessel data into it. Max 10 megabytes
        /// </summary>
        private static readonly byte[] VesselSerializedBytes = new byte[10 * 1024 * 1000];

        private static readonly object VesselArraySyncLock = new object();

        private const int ProtoSerializeRetryMaxAttempts = 6;
        private const int ProtoSerializeRetryFrameDelay = 2;

        public void SendMessage(IMessageData msg)
        {
            NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<VesselCliMsg>(msg));
        }

        /// <summary>
        /// Sends a vessel's proto/definition to the server.
        /// <paramref name="reason"/> is a human-readable description (e.g. "Flight ready (launch)", "Part decoupled")
        /// used by the server's craft create/remove audit log the first time a vessel is registered.
        /// </summary>
        public void SendVesselMessage(Vessel vessel, bool forceReload = false, string reason = null)
        {
            if (vessel == null || vessel.state == Vessel.State.DEAD || VesselRemoveSystem.Singleton.VesselWillBeKilled(vessel.id))
                return;

            if (!vessel.orbitDriver)
            {
                LunaLog.LogWarning($"Cannot send vessel {vessel.vesselName} - {vessel.id}. It's orbit driver is null!");
                return;
            }

            if (vessel.orbitDriver.Ready())
            {
                vessel.protoVessel = vessel.BackupVessel();
                SendVesselMessage(vessel.protoVessel, forceReload, reason);
            }
            else
            {
                //Orbit driver is not ready so wait max 10 frames until it's ready
                CoroutineUtil.StartConditionRoutine("SendVesselMessage",
                    () => SendVesselMessage(vessel, forceReload, reason),
                    () => vessel.orbitDriver.Ready(), 10);
            }
        }

        #region Private methods

        private void SendVesselMessage(ProtoVessel protoVessel, bool forceReload, string reason)
        {
            if (protoVessel == null || protoVessel.vesselID == Guid.Empty) return;
            //Doing this in another thread can crash the game as during the serialization into a config node Lingoona is called...
            //TODO: Check if this works fine with the new unity version as it used to crash....
            TaskFactory.StartNew(() => PrepareAndSendProtoVessel(protoVessel, forceReload, reason));
            //PrepareAndSendProtoVessel(protoVessel);
        }

        /// <summary>
        /// This method prepares the protovessel class and send the message, it's intended to be run in another thread
        /// </summary>
        private void PrepareAndSendProtoVessel(ProtoVessel protoVessel, bool forceReload, string reason)
        {
            //Never send empty vessel id's (it happens with flags...)
            if (protoVessel.vesselID == Guid.Empty) return;

            //VesselSerializedBytes is shared so lock it!
            lock (VesselArraySyncLock)
            {
                VesselSerializer.SerializeVesselToArray(protoVessel, VesselSerializedBytes, out var numBytes);
                if (numBytes > 0)
                {
                    var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<VesselProtoMsgData>();
                    msgData.GameTime = TimeSyncSystem.UniversalTime;
                    msgData.VesselId = protoVessel.vesselID;
                    msgData.NumBytes = numBytes;
                    msgData.ForceReload = forceReload;
                    msgData.Reason = reason;
                    if (msgData.Data.Length < numBytes)
                        Array.Resize(ref msgData.Data, numBytes);
                    Array.Copy(VesselSerializedBytes, 0, msgData.Data, 0, numBytes);

                    SendMessage(msgData);
                }
                else
                {
                    if (protoVessel.vesselType == VesselType.Debris)
                    {
                        LunaLog.Log($"Serialization of debris vessel: {protoVessel.vesselID} name: {protoVessel.vesselName} failed. Adding to kill list");
                        VesselRemoveSystem.Singleton.KillVessel(protoVessel.vesselID, true, "Serialization of debris failed");
                    }
                    else
                    {
                        var retryVesselId = protoVessel.vesselID;
                        var retryForceReload = forceReload;
                        var retryReason = reason;
                        LunaLog.LogWarning(
                            $"[LMP]: Proto serialize produced 0 bytes for {retryVesselId} ({protoVessel.vesselName}); scheduling main-thread retry (NaN orbit / Save failure).");
                        MainSystem.EnqueueMainThreadAction(() => BeginSerializeRetry(retryVesselId, retryForceReload, retryReason));
                    }
                }
            }
        }

        /// <summary>
        /// Re-attempts <see cref="SendVesselMessage(Vessel,bool,string)"/> from the Unity thread after a background
        /// serialize produced 0 bytes (e.g. transient NaN right after decouple).
        /// </summary>
        internal void BeginSerializeRetry(Guid vesselId, bool forceReload, string reason)
        {
            ScheduleSerializeRetryAfterFrames(vesselId, forceReload, reason, 0, ProtoSerializeRetryFrameDelay);
        }

        private void ScheduleSerializeRetryAfterFrames(Guid vesselId, bool forceReload, string reason, int attemptIndex, int framesDelay)
        {
            if (attemptIndex >= ProtoSerializeRetryMaxAttempts)
            {
                LunaLog.LogWarning($"[LMP]: Gave up retrying proto send for vessel {vesselId} after {ProtoSerializeRetryMaxAttempts} attempts.");
                return;
            }

            CoroutineUtil.StartFrameDelayedRoutine($"ProtoSerializeRetry_{vesselId}_{attemptIndex}", () =>
            {
                var vessel = FlightGlobals.FindVessel(vesselId);
                if (vessel == null || vessel.state == Vessel.State.DEAD || VesselRemoveSystem.Singleton.VesselWillBeKilled(vesselId))
                {
                    ScheduleSerializeRetryAfterFrames(vesselId, forceReload, reason, attemptIndex + 1, ProtoSerializeRetryFrameDelay);
                    return;
                }

                if (!vessel.orbitDriver || !vessel.orbitDriver.Ready())
                {
                    ScheduleSerializeRetryAfterFrames(vesselId, forceReload, reason, attemptIndex + 1, ProtoSerializeRetryFrameDelay);
                    return;
                }

                ProtoVessel backup;
                try
                {
                    backup = vessel.BackupVessel();
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[LMP]: BackupVessel failed on proto retry for {vesselId}: {ex.Message}");
                    ScheduleSerializeRetryAfterFrames(vesselId, forceReload, reason, attemptIndex + 1, ProtoSerializeRetryFrameDelay);
                    return;
                }

                var cfg = new ConfigNode();
                try
                {
                    backup.Save(cfg);
                }
                catch (Exception ex)
                {
                    LunaLog.LogWarning($"[LMP]: Proto Save failed on retry for {vesselId}: {ex.Message}");
                    ScheduleSerializeRetryAfterFrames(vesselId, forceReload, reason, attemptIndex + 1, ProtoSerializeRetryFrameDelay);
                    return;
                }

                if (cfg.VesselHasNaNPosition())
                {
                    ScheduleSerializeRetryAfterFrames(vesselId, forceReload, reason, attemptIndex + 1, ProtoSerializeRetryFrameDelay);
                    return;
                }

                SendVesselMessage(vessel, forceReload, reason);
            }, framesDelay);
        }

        #endregion
    }
}
