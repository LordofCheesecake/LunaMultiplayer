using LmpCommon.Message.Data.Vessel;
using LunaConfigNode;
using Server.Utilities;
using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace Server.System.Vessel
{
    /// <summary>
    /// We try to avoid working with protovessels as much as possible as they can be huge files.
    /// This class patches the vessel file with the information messages we receive about a position and other vessel properties.
    /// This way we send the whole vessel definition only when there are parts that have changed 
    /// </summary>
    public partial class VesselDataUpdater
    {
        /// <summary>
        /// Update the vessel files with resource data max at a 2,5 seconds interval
        /// </summary>
        private const int FileResourcesUpdateIntervalMs = 2500;

        /// <summary>
        /// Avoid updating the vessel files so often as otherwise the server will lag a lot!
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, DateTime> LastResourcesUpdateDictionary = new ConcurrentDictionary<Guid, DateTime>();


        /// <summary>
        /// We received a resource information from a player
        /// Then we rewrite the vesselproto with that last information so players that connect later received an update vesselproto
        /// </summary>
        public static void WriteResourceDataToFile(VesselBaseMsgData message)
        {
            if (!(message is VesselResourceMsgData msgData)) return;
            if (VesselContext.RemovedVessels.ContainsKey(msgData.VesselId)) return;

            if (!LastResourcesUpdateDictionary.TryGetValue(msgData.VesselId, out var lastUpdated) || (DateTime.Now - lastUpdated).TotalMilliseconds > FileResourcesUpdateIntervalMs)
            {
                LastResourcesUpdateDictionary.AddOrUpdate(msgData.VesselId, DateTime.Now, (key, existingVal) => DateTime.Now);

                BackgroundWork.Fire(() =>
                {
                    lock (Semaphore.GetOrAdd(msgData.VesselId, new object()))
                    {
                        if (!VesselStoreSystem.CurrentVessels.TryGetValue(msgData.VesselId, out var vessel)) return;

                        // Resources is an array-pooled buffer whose Length may exceed ResourcesCount when a
                        // previous message had more entries; iterate only the live slice and null-guard each
                        // entry before dereferencing to avoid the NullReferenceException that was causing
                        // partial proto-vessel writes (and downstream physics desync / explosions).
                        var count = msgData.ResourcesCount;
                        var resources = msgData.Resources;
                        if (resources == null) return;
                        if (count > resources.Length) count = resources.Length;

                        for (var i = 0; i < count; i++)
                        {
                            var resource = resources[i];
                            if (resource == null || string.IsNullOrEmpty(resource.ResourceName)) continue;

                            var part = vessel.GetPart(resource.PartFlightId);
                            if (part?.Resources == null) continue;

                            // Resolve the first matching resource node. We avoid MixedCollection.GetSingle
                            // because it throws on duplicate keys (legitimate for some modded parts) which
                            // would abort the whole update and corrupt the proto-vessel snapshot on disk.
                            ConfigNode resourceNode = null;
                            foreach (var entry in part.Resources.GetAll())
                            {
                                if (entry.Key != resource.ResourceName) continue;
                                resourceNode = entry.Value;
                                break;
                            }

                            if (resourceNode == null) continue;

                            resourceNode.UpdateValue("amount", resource.Amount.ToString(CultureInfo.InvariantCulture));
                            resourceNode.UpdateValue("flowState", resource.FlowState.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                });
            }
        }
    }
}
