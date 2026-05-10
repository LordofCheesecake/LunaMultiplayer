using LunaConfigNode.CfgNode;
using Server.Log;
using Server.Settings.Structures;
using Server.Utilities;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Server.System.Vessel
{
    /// <summary>
    /// We try to avoid working with protovessels as much as possible as they can be huge files.
    /// This class patches the vessel file with the information messages we receive about a position and other vessel properties.
    /// This way we send the whole vessel definition only when there are parts that have changed 
    /// </summary>
    public partial class VesselDataUpdater
    {
        #region Semaphore

        /// <summary>
        /// To not overwrite our own data we use a lock
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, object> Semaphore = new ConcurrentDictionary<Guid, object>();

        /// <summary>
        /// Highest <see cref="LmpCommon.Message.Data.Vessel.VesselBaseMsgData.GameTime"/> successfully written to
        /// <see cref="VesselStoreSystem.CurrentVessels"/> for a given vessel. Used to reject out-of-order full-proto
        /// overwrites and to fast-reject obviously stale protos without scheduling work. Updated only inside the
        /// per-vessel lock after a successful merge so a rejected proto (e.g. mod-control) does not advance this
        /// value and block later snapshots with lower <c>GameTime</c>.
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, double> LastAppliedProtoGameTime = new ConcurrentDictionary<Guid, double>();

        /// <summary>
        /// Rate-limits debug logs when position/update traffic targets a vessel id not yet in <see cref="VesselStoreSystem.CurrentVessels"/>.
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, DateTime> LastUnknownVesselStoreDebugLogUtc = new ConcurrentDictionary<Guid, DateTime>();

        private static readonly TimeSpan UnknownVesselStoreLogMinInterval = TimeSpan.FromSeconds(30);

        #endregion

        /// <summary>
        /// Sets ORBIT IDENT from the reference body name when provided (e.g. from position or update messages).
        /// </summary>
        internal static void ApplyOrbitIdent(Classes.Vessel vessel, string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;

            if (vessel.Orbit.Exists("IDENT"))
                vessel.Orbit.Update("IDENT", bodyName);
            else
                vessel.Orbit.Add(new CfgNodeValue<string, string>("IDENT", bodyName));
        }

        /// <summary>
        /// Returns the per-vessel lock object used to serialize mutations inside the <see cref="VesselStoreSystem.CurrentVessels"/>
        /// dictionary. Other systems (e.g. <see cref="VesselStoreSystem.BackupVessels"/> / <see cref="VesselStoreSystem.PersistVesselToFile"/>)
        /// can acquire it around <see cref="Classes.Vessel.ToString"/> to avoid torn serialization while a partial-update <see cref="Task.Run"/>
        /// is mutating the same tree.
        /// </summary>
        internal static object GetVesselLock(Guid vesselId) => Semaphore.GetOrAdd(vesselId, _ => new object());

        /// <summary>
        /// Drops per-vessel bookkeeping. Call this whenever a vessel is permanently removed so the
        /// <see cref="Semaphore"/>, <see cref="LastAppliedProtoGameTime"/>, and the per-message-type throttle
        /// dictionaries do not grow unbounded over long server uptimes, and so a later legitimate re-creation
        /// of a vessel with the same id is not blocked by a stale timestamp or stale throttle.
        /// </summary>
        internal static void ForgetVessel(Guid vesselId)
        {
            Semaphore.TryRemove(vesselId, out _);
            LastAppliedProtoGameTime.TryRemove(vesselId, out _);
            LastUpdateDictionary.TryRemove(vesselId, out _);
            LastPositionUpdateDictionary.TryRemove(vesselId, out _);
            LastFlightStateUpdateDictionary.TryRemove(vesselId, out _);
            LastResourcesUpdateDictionary.TryRemove(vesselId, out _);
            LastUnknownVesselStoreDebugLogUtc.TryRemove(vesselId, out _);
        }

        /// <summary>
        /// Emits at most one debug line per <see cref="UnknownVesselStoreLogMinInterval"/> per vessel when partial
        /// updates cannot apply because the vessel is not in the server store yet (typical right after decouple if
        /// the full proto never arrived).
        /// </summary>
        internal static void LogIfVesselMissingFromStore(Guid vesselId, string updateKind)
        {
            var nowUtc = DateTime.UtcNow;
            if (LastUnknownVesselStoreDebugLogUtc.TryGetValue(vesselId, out var lastLoggedUtc) &&
                nowUtc - lastLoggedUtc < UnknownVesselStoreLogMinInterval)
            {
                return;
            }

            LastUnknownVesselStoreDebugLogUtc[vesselId] = nowUtc;
            LunaLog.Debug($"Vessel {updateKind} for {vesselId} skipped: not in CurrentVessels (no full proto registered yet).");
        }

        /// <summary>
        /// Raw updates a vessel in the dictionary and takes care of the locking in case we received another vessel message type.
        /// Protos strictly older than the latest one already applied are dropped without scheduling work; under the
        /// per-vessel lock, a proto superseded while parsing is discarded. <see cref="LastAppliedProtoGameTime"/> is
        /// advanced only when the store is actually updated.
        /// </summary>
        /// <param name="vesselId">Target vessel id.</param>
        /// <param name="gameTime">In-game timestamp (<see cref="LmpCommon.Message.Data.Vessel.VesselBaseMsgData.GameTime"/>) of the incoming proto.</param>
        /// <param name="vesselDataInConfigNodeFormat">Proto vessel in KSP ConfigNode text format.</param>
        /// <returns><c>true</c> if the proto was accepted and scheduled for apply; <c>false</c> if it was dropped as stale.</returns>
        public static bool RawConfigNodeInsertOrUpdate(Guid vesselId, double gameTime, string vesselDataInConfigNodeFormat)
        {
            var incomingGameTime = gameTime;

            if (LastAppliedProtoGameTime.TryGetValue(vesselId, out var committedLatest) && incomingGameTime < committedLatest)
            {
                LunaLog.Debug($"Ignored out-of-order proto for vessel {vesselId} (gameTime {incomingGameTime:F3})");
                return false;
            }

            BackgroundWork.Fire(() =>
            {
                Classes.Vessel vessel;
                try
                {
                    vessel = new Classes.Vessel(vesselDataInConfigNodeFormat);
                }
                catch (Exception ex)
                {
                    LunaLog.Warning($"Failed to parse vessel proto {vesselId}: {ex.Message}");
                    return;
                }

                if (GeneralSettings.SettingsStore.ModControl)
                {
                    var vesselParts = vessel.Parts.GetAllValues().Select(p => p.Fields.GetSingle("name").Value);
                    var bannedParts = vesselParts.Except(ModFileSystem.ModControl.AllowedParts);
                    if (bannedParts.Any())
                    {
                        LunaLog.Warning($"Received a vessel with BANNED parts! {vesselId}");
                        return;
                    }
                }

                lock (GetVesselLock(vesselId))
                {
                    if (LastAppliedProtoGameTime.TryGetValue(vesselId, out var latestUnderLock) && incomingGameTime < latestUnderLock)
                    {
                        LunaLog.Debug($"Discarding proto for vessel {vesselId} superseded during parse (gameTime {incomingGameTime:F3} < {latestUnderLock:F3})");
                        return;
                    }

                    VesselStoreSystem.CurrentVessels.AddOrUpdate(vesselId, vessel, (_, _) => vessel);
                    LastAppliedProtoGameTime[vesselId] = incomingGameTime;
                }
            });

            return true;
        }
    }
}
