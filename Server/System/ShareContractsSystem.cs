using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Log;
using Server.Server;
using Server.System.Scenario;
using Server.Utilities;

namespace Server.System
{
    public static class ShareContractsSystem
    {
        /// <summary>
        /// Coalesce contract-file writes within this window. Contract payloads can be large and are frequently
        /// updated (every accepted/declined contract fires an update); without debouncing, each triggers a
        /// full file rewrite on the scenario disk.
        /// </summary>
        private const int ContractWriteDebounceMs = 500;

        public static void ContractsReceived(ClientStructure client, ShareProgressContractsMsgData data)
        {
            // Previously logged each individual contract GUID, which can be hundreds of lines for a full sync.
            // Summarize instead to keep the log readable on busy servers.
            LunaLog.Debug($"Contract data received: {data.Contracts?.Length ?? 0} contracts from {client.PlayerName}");

            //send the contract update to all other clients
            MessageQueuer.RelayMessage<ShareProgressSrvMsg>(client, data);

            // Debounce the file write so bursts of contract changes collapse into a single disk flush.
            Debouncer.Trigger("scenario-contracts", ContractWriteDebounceMs,
                () => ScenarioDataUpdater.WriteContractDataToFile(data));
        }
    }
}
