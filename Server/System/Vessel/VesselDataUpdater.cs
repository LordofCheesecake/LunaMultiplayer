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
        /// Highest <see cref="LmpCommon.Message.Data.Vessel.VesselBaseMsgData.GameTime"/> we have ever scheduled to apply
        /// for a given vessel. Used to reject out-of-order full-proto overwrites that would otherwise wipe out
        /// newer partial updates (position/update/resource/part sync fields) applied between the stale snapshot
        /// and its late arrival. Guarded atomically via <see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate(TKey,TValue,Func{TKey,TValue,TValue})"/>.
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, double> LastAppliedProtoGameTime = new ConcurrentDictionary<Guid, double>();

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
        }

        /// <summary>
        /// Raw updates a vessel in the dictionary and takes care of the locking in case we received another vessel message type.
        /// Protos strictly older than the latest one already scheduled for this vessel are dropped to prevent
        /// stale full-snapshot overwrites from wiping out newer partial updates.
        /// </summary>
        /// <param name="vesselId">Target vessel id.</param>
        /// <param name="gameTime">In-game timestamp (<see cref="LmpCommon.Message.Data.Vessel.VesselBaseMsgData.GameTime"/>) of the incoming proto.</param>
        /// <param name="vesselDataInConfigNodeFormat">Proto vessel in KSP ConfigNode text format.</param>
        /// <returns><c>true</c> if the proto was accepted and scheduled for apply; <c>false</c> if it was dropped as stale.</returns>
        public static bool RawConfigNodeInsertOrUpdate(Guid vesselId, double gameTime, string vesselDataInConfigNodeFormat)
        {
            var incomingGameTime = gameTime;
            var isStale = false;

            LastAppliedProtoGameTime.AddOrUpdate(vesselId, incomingGameTime, (_, existing) =>
            {
                if (incomingGameTime < existing)
                {
                    isStale = true;
                    return existing;
                }
                return incomingGameTime;
            });

            if (isStale)
            {
                LunaLog.Debug($"Ignored out-of-order proto for vessel {vesselId} (gameTime {incomingGameTime:F3})");
                return false;
            }

            BackgroundWork.Fire(() =>
            {
                var vessel = new Classes.Vessel(vesselDataInConfigNodeFormat);
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
                    // Re-check under the lock: a newer proto may have been accepted while we were parsing.
                    if (LastAppliedProtoGameTime.TryGetValue(vesselId, out var latest) && incomingGameTime < latest)
                    {
                        LunaLog.Debug($"Discarding proto for vessel {vesselId} superseded during parse (gameTime {incomingGameTime:F3} < {latest:F3})");
                        return;
                    }
                    VesselStoreSystem.CurrentVessels.AddOrUpdate(vesselId, vessel, (key, existingVal) => vessel);
                }
            });

            return true;
        }
    }
}
