using Contracts;
using LmpClient.Events;
using LmpClient.Systems.Lock;
using LmpClient.Systems.ShareProgress;
using LmpCommon.Enums;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsSystem : ShareProgressBaseSystem<ShareContractsSystem, ShareContractsMessageSender, ShareContractsMessageHandler>
    {
        public override string SystemName { get; } = nameof(ShareContractsSystem);

        private ShareContractsEvents ShareContractsEvents { get; } = new ShareContractsEvents();

        public int DefaultContractGenerateIterations;

        /// <summary>
        /// Populated by <see cref="ScenarioSystem"/> just before the ContractSystem scenario is
        /// loaded. Keys are GUID strings of Offered contracts in the server's snapshot; values are
        /// the original contract type name and, for part-validation failures, the missing part name.
        ///
        /// <see cref="ShareContractsEvents.ContractsLoaded"/> compares this set against what
        /// actually loaded into <see cref="ContractSystem.Instance"/> and creates
        /// <see cref="LmpUnavailableContract"/> stubs for any GUIDs that are absent.
        /// </summary>
        internal Dictionary<string, (string TypeName, string MissingAsset)> PendingUnavailableContracts { get; }
            = new Dictionary<string, (string, string)>();

        /// <summary>
        /// Records the Offered contracts from the ContractSystem scenario node so that stubs can
        /// be created for any that fail to load. Called from <see cref="ScenarioSystem"/> before
        /// the node is handed to KSP.
        /// </summary>
        /// <param name="scenarioNode">The ContractSystem ConfigNode received from the server.</param>
        /// <param name="strippedWithMissingPart">
        /// Map of GUID → missing part name for contracts already stripped by the part-validation
        /// pre-filter. May be null if pre-filtering did not run.
        /// </param>
        public void PrepareUnavailableContractStubs(ConfigNode scenarioNode,
            IReadOnlyDictionary<string, (string TypeName, string MissingAsset)> strippedWithMissingPart)
        {
            PendingUnavailableContracts.Clear();

            var contractsNode = scenarioNode.GetNode("CONTRACTS");
            if (contractsNode != null)
            {
                foreach (var contractNode in contractsNode.GetNodes("CONTRACT"))
                {
                    var guid = contractNode.GetValue("guid");
                    var typeName = contractNode.GetValue("type") ?? "Unknown";
                    var state = contractNode.GetValue("state");

                    if (string.IsNullOrEmpty(guid) || state != "Offered") continue;

                    PendingUnavailableContracts[guid] = (typeName, null);
                }
            }

            // Stripped contracts were removed from the node before KSP sees them, so they never
            // appear in the iteration above. Track them separately so stubs are created for them.
            if (strippedWithMissingPart != null)
            {
                foreach (var kvp in strippedWithMissingPart)
                    PendingUnavailableContracts[kvp.Key] = kvp.Value;
            }

            LunaLog.Log($"[ShareContracts]: Tracking {PendingUnavailableContracts.Count} Offered contracts from server snapshot for unavailability detection.");
        }

        //This queue system is not used because we use one big queue in ShareCareerSystem for this system.
        protected override bool ShareSystemReady => true;

        protected override GameMode RelevantGameModes => GameMode.Career;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            if (!CurrentGameModeIsRelevant) return;

            ContractSystem.generateContractIterations = 0;

            // Protect the startup window: any ContractOffered events that fire between
            // system enable and the scene being GUI-ready (when TryGetContractLock runs)
            // must not kill server contracts. This matters on servers with no active lock
            // holder, where contracts can only arrive via ContractSystem.OnLoad() and any
            // post-load re-offer events (e.g. from ContractPreLoader or mod initialisation).
            // Cleared in LevelLoaded() after lock status is determined.
            IgnoreEvents = true;

            LockEvent.onLockAcquire.Add(ShareContractsEvents.LockAcquire);
            LockEvent.onLockRelease.Add(ShareContractsEvents.LockReleased);
            GameEvents.onLevelWasLoadedGUIReady.Add(ShareContractsEvents.LevelLoaded);

            GameEvents.Contract.onAccepted.Add(ShareContractsEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Add(ShareContractsEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Add(ShareContractsEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Add(ShareContractsEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Add(ShareContractsEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Add(ShareContractsEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Add(ShareContractsEvents.ContractFailed);
            GameEvents.Contract.onFinished.Add(ShareContractsEvents.ContractFinished);
            GameEvents.Contract.onOffered.Add(ShareContractsEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Add(ShareContractsEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Add(ShareContractsEvents.ContractRead);
            GameEvents.Contract.onSeen.Add(ShareContractsEvents.ContractSeen);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            PendingUnavailableContracts.Clear();
            ContractSystem.generateContractIterations = DefaultContractGenerateIterations;

            LockEvent.onLockAcquire.Remove(ShareContractsEvents.LockAcquire);
            LockEvent.onLockRelease.Remove(ShareContractsEvents.LockReleased);
            GameEvents.onLevelWasLoadedGUIReady.Remove(ShareContractsEvents.LevelLoaded);

            //Always try to remove the event, as when we disconnect from a server the server settings will get the default values
            GameEvents.Contract.onAccepted.Remove(ShareContractsEvents.ContractAccepted);
            GameEvents.Contract.onCancelled.Remove(ShareContractsEvents.ContractCancelled);
            GameEvents.Contract.onCompleted.Remove(ShareContractsEvents.ContractCompleted);
            GameEvents.Contract.onContractsListChanged.Remove(ShareContractsEvents.ContractsListChanged);
            GameEvents.Contract.onContractsLoaded.Remove(ShareContractsEvents.ContractsLoaded);
            GameEvents.Contract.onDeclined.Remove(ShareContractsEvents.ContractDeclined);
            GameEvents.Contract.onFailed.Remove(ShareContractsEvents.ContractFailed);
            GameEvents.Contract.onFinished.Remove(ShareContractsEvents.ContractFinished);
            GameEvents.Contract.onOffered.Remove(ShareContractsEvents.ContractOffered);
            GameEvents.Contract.onParameterChange.Remove(ShareContractsEvents.ContractParameterChanged);
            GameEvents.Contract.onRead.Remove(ShareContractsEvents.ContractRead);
            GameEvents.Contract.onSeen.Remove(ShareContractsEvents.ContractSeen);
        }

        /// <summary>
        /// Try to acquire the contract lock
        /// </summary>
        public void TryGetContractLock()
        {
            if (!LockSystem.LockQuery.ContractLockExists())
            {
                LockSystem.Singleton.AcquireContractLock();
            }
        }
    }
}
