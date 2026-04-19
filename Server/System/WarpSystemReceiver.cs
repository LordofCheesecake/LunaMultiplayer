using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;

namespace Server.System
{
    public class WarpSystemReceiver
    {
        private static readonly object CreateSubspaceLock = new object();

        // Sanity bounds for the client-supplied ServerTimeDifference (seconds). Subspaces represent an in-game universe
        // time offset, so a few centuries in either direction is already well beyond any legitimate KSP save. We cap to
        // prevent a buggy/malicious client from jumping everyone billions of years.
        private const double MinServerTimeDifferenceSeconds = -1_000_000_000d; // ~-31 years
        private const double MaxServerTimeDifferenceSeconds =  100_000_000_000d; // ~3170 years forward

        public void HandleNewSubspace(ClientStructure client, WarpNewSubspaceMsgData message)
        {
            lock (CreateSubspaceLock)
            {
                if (message.PlayerCreator != client.PlayerName) return;

                // Reject NaN/Infinity and values outside reasonable bounds.
                var serverTimeDifference = message.ServerTimeDifference;
                if (double.IsNaN(serverTimeDifference) || double.IsInfinity(serverTimeDifference) ||
                    serverTimeDifference < MinServerTimeDifferenceSeconds ||
                    serverTimeDifference > MaxServerTimeDifferenceSeconds)
                {
                    LunaLog.Warning($"Rejecting subspace from {client.PlayerName}: ServerTimeDifference {serverTimeDifference} out of range");
                    return;
                }

                // Reject subspace that claims to be earlier than the current latest subspace; subspaces are meant to
                // represent advancement in universe time, not jumps backward into "the shared past".
                var latest = WarpContext.LatestSubspace;
                if (latest != null && serverTimeDifference < latest.Time)
                {
                    LunaLog.Warning($"Rejecting subspace from {client.PlayerName}: offset {serverTimeDifference} earlier than current latest {latest.Time}. Force-syncing to subspace {latest.Id}.");
                    ForceSyncClientToSubspace(client, latest.Id);
                    return;
                }

                LunaLog.Debug($"{client.PlayerName} created the new subspace '{WarpContext.NextSubspaceId}'");

                //Create Subspace
                WarpContext.Subspaces.TryAdd(WarpContext.NextSubspaceId, new Subspace(WarpContext.NextSubspaceId, serverTimeDifference, client.PlayerName));

                //Tell all Clients about the new Subspace
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpNewSubspaceMsgData>();
                msgData.ServerTimeDifference = serverTimeDifference;
                msgData.PlayerCreator = message.PlayerCreator;
                msgData.SubspaceKey = WarpContext.NextSubspaceId;

                MessageQueuer.SendToAllClients<WarpSrvMsg>(msgData);
                WarpContext.NextSubspaceId++;
            }
        }

        public void HandleChangeSubspace(ClientStructure client, WarpChangeSubspaceMsgData message)
        {
            if (message.PlayerName != client.PlayerName) return;

            var oldSubspace = client.Subspace;
            var newSubspace = message.Subspace;

            if (oldSubspace != newSubspace)
            {
                if (newSubspace < 0)
                {
                    LunaLog.Debug($"{client.PlayerName} is warping");
                }
                else if (!WarpContext.Subspaces.TryGetValue(newSubspace, out var newSubspaceEntry))
                {
                    // Client referenced a subspace the server does not know about; reject rather than crash.
                    LunaLog.Warning($"{client.PlayerName} requested unknown subspace '{newSubspace}' - ignoring");
                    return;
                }
                else if (newSubspaceEntry.Creator != client.PlayerName)
                {
                    LunaLog.Debug($"{client.PlayerName} synced with subspace '{message.Subspace}' created by {newSubspaceEntry.Creator}");
                }

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpChangeSubspaceMsgData>();
                msgData.PlayerName = client.PlayerName;
                msgData.Subspace = message.Subspace;

                MessageQueuer.RelayMessage<WarpSrvMsg>(client, msgData);

                if (newSubspace != -1)
                {
                    client.Subspace = newSubspace;

                    //Try to remove their old subspace
                    WarpSystem.RemoveSubspace(oldSubspace);
                }
            }
        }

        public void HandleSubspaceRequest(ClientStructure client)
        {
            lock (CreateSubspaceLock)
            {
                WarpSystemSender.SendAllSubspaces(client);
            }
        }

        /// <summary>
        /// Broadcasts a <see cref="WarpChangeSubspaceMsgData"/> placing <paramref name="client"/> on
        /// <paramref name="subspaceId"/>. Used when the server rejects a NewSubspace that would jump backwards
        /// in universe time: without this the offending client stays stuck on an older subspace, all other
        /// clients continue rejecting its vessel stream, and vessels desync / explode.
        /// </summary>
        private static void ForceSyncClientToSubspace(ClientStructure client, int subspaceId)
        {
            if (client == null) return;

            var oldSubspace = client.Subspace;
            client.Subspace = subspaceId;

            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<WarpChangeSubspaceMsgData>();
            msgData.PlayerName = client.PlayerName;
            msgData.Subspace = subspaceId;

            // Inform everyone (including the offending client) that this player is now on the authoritative
            // subspace. Clients treat a ChangeSubspace targeting their own PlayerName as an authoritative sync.
            MessageQueuer.SendToAllClients<WarpSrvMsg>(msgData);

            // Try to remove the stale subspace if nobody is on it anymore (mirrors HandleChangeSubspace).
            if (oldSubspace != subspaceId && oldSubspace >= 0)
                WarpSystem.RemoveSubspace(oldSubspace);
        }
    }
}
