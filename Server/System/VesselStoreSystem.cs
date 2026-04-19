using LunaConfigNode;
using Server.Context;
using Server.System.Vessel;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System
{
    /// <summary>
    /// Here we keep a copy of all the player vessels in <see cref="Vessel"/> format and we also save them to files at a specified rate
    /// </summary>
    public static class VesselStoreSystem
    {
        public const string VesselFileFormat = ".txt";
        public static string VesselsPath = Path.Combine(ServerContext.UniverseDirectory, "Vessels");

        public static ConcurrentDictionary<Guid, Vessel.Classes.Vessel> CurrentVessels = new ConcurrentDictionary<Guid, Vessel.Classes.Vessel>();

        private static readonly object BackupLock = new object();

        public static bool VesselExists(Guid vesselId) => CurrentVessels.ContainsKey(vesselId);

        /// <summary>
        /// Removes a vessel from the store
        /// </summary>
        public static void RemoveVessel(Guid vesselId)
        {
            CurrentVessels.TryRemove(vesselId, out _);

            // Drop per-vessel bookkeeping (lock object + last-applied proto gameTime) so a later re-creation
            // of a vessel with the same id is not blocked by a stale timestamp, and so these dictionaries
            // do not grow unbounded over long server uptimes.
            VesselDataUpdater.ForgetVessel(vesselId);

            _ = Task.Run(() =>
            {
                lock (BackupLock)
                {
                    FileHandler.FileDelete(Path.Combine(VesselsPath, $"{vesselId}{VesselFileFormat}"));
                }
            });
        }

        /// <summary>
        /// Returns a vessel in the standard KSP format
        /// </summary>
        public static string GetVesselInConfigNodeFormat(Guid vesselId)
        {
            return CurrentVessels.TryGetValue(vesselId, out var vessel) ?
                vessel.ToString() : null;
        }

        /// <summary>
        /// Load the stored vessels into the dictionary
        /// </summary>
        public static void LoadExistingVessels()
        {
            ChangeExistingVesselFormats();
            lock (BackupLock)
            {
                foreach (var file in Directory.GetFiles(VesselsPath).Where(f => Path.GetExtension(f) == VesselFileFormat))
                {
                    if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var vesselId))
                    {
                        CurrentVessels.TryAdd(vesselId, new Vessel.Classes.Vessel(FileHandler.ReadFileText(file)));
                    }
                }
            }
        }

        /// <summary>
        /// Transform OLD Xml vessels into the new format
        /// TODO: Remove this for next version
        /// </summary>
        public static void ChangeExistingVesselFormats()
        {
            lock (BackupLock)
            {
                foreach (var file in Directory.GetFiles(VesselsPath).Where(f => Path.GetExtension(f) == ".xml"))
                {
                    if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var vesselId))
                    {
                        var vesselAsCfgNode = XmlConverter.ConvertToConfigNode(FileHandler.ReadFileText(file));
                        FileHandler.WriteToFile(file.Replace(".xml", ".txt"), vesselAsCfgNode);
                    }
                    FileHandler.FileDelete(file);
                }
            }
        }

        /// <summary>
        /// Actually performs the backup of the vessels to file.
        /// Each vessel is serialized while holding its per-vessel lock (shared with <see cref="VesselDataUpdater"/>)
        /// so a concurrent partial-update <see cref="Task"/> cannot mutate the same ConfigNode tree mid-walk,
        /// which would otherwise produce a subtly inconsistent <c>.txt</c> on disk and cause vessels to
        /// "revert" after a server restart. The disk write itself happens outside the per-vessel lock but
        /// still inside <see cref="BackupLock"/> to keep file I/O serialized.
        /// </summary>
        public static void BackupVessels()
        {
            lock (BackupLock)
            {
                var vesselsInCfgNode = CurrentVessels.ToArray();
                foreach (var vessel in vesselsInCfgNode)
                {
                    string serialized;
                    lock (VesselDataUpdater.GetVesselLock(vessel.Key))
                    {
                        serialized = vessel.Value.ToString();
                    }
                    FileHandler.WriteToFile(Path.Combine(VesselsPath, $"{vessel.Key}{VesselFileFormat}"), serialized);
                }
            }
        }

        /// <summary>
        /// Writes one vessel to disk so live patches (orbit, IDENT, position fields) are reflected in the Vessels folder without waiting for <see cref="BackupVessels"/>.
        /// Serializes under the per-vessel lock for the same reason as <see cref="BackupVessels"/>.
        /// </summary>
        public static void PersistVesselToFile(Guid vesselId)
        {
            if (!CurrentVessels.TryGetValue(vesselId, out var vessel)) return;

            string serialized;
            lock (VesselDataUpdater.GetVesselLock(vesselId))
            {
                serialized = vessel.ToString();
            }

            lock (BackupLock)
            {
                FileHandler.WriteToFile(Path.Combine(VesselsPath, $"{vesselId}{VesselFileFormat}"), serialized);
            }
        }
    }
}
