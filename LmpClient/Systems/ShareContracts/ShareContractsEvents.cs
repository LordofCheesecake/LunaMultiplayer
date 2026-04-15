using Contracts;
using Contracts.Templates;
using LmpClient.Base;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Locks;
using System;
using System.Collections.Generic;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsEvents : SubSystem<ShareContractsSystem>
    {
        /// <summary>
        /// If we get the contract lock then generate contracts
        /// </summary>
        public void LockAcquire(LockDefinition lockDefinition)
        {
            if (lockDefinition.Type == LockType.Contract && lockDefinition.PlayerName == SettingsSystem.CurrentSettings.PlayerName)
            {
                ContractSystem.generateContractIterations = ShareContractsSystem.Singleton.DefaultContractGenerateIterations;
            }
        }

        /// <summary>
        /// Try to get contract lock
        /// </summary>
        public void LockReleased(LockDefinition lockDefinition)
        {
            if (lockDefinition.Type == LockType.Contract)
            {
                System.TryGetContractLock();
            }
        }

        /// <summary>
        /// Try to get contract lock when loading a level
        /// </summary>
        public void LevelLoaded(GameScenes data)
        {
            System.TryGetContractLock();
            // StopIgnoringEvents is deferred to ContractsLoaded() so the guard covers the full
            // ContractSystem.OnLoad() window. onContractsLoaded fires after OnLoad completes,
            // while onLevelWasLoadedGUIReady can fire up to ~100ms before contracts finish loading.
        }

        #region EventHandlers

        public void ContractAccepted(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractMessage(contract);
            LunaLog.Log($"Contract accepted: {contract.ContractGuid}");
        }

        public void ContractCancelled(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractMessage(contract);
            LunaLog.Log($"Contract cancelled: {contract.ContractGuid}");
        }

        public void ContractCompleted(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractMessage(contract);
            LunaLog.Log($"Contract completed: {contract.ContractGuid}");
        }

        public void ContractsListChanged()
        {
            LunaLog.Log("Contract list changed.");
        }

        public void ContractsLoaded()
        {
            LunaLog.Log("Contracts loaded.");
            // Safe point to stop ignoring events: ContractSystem.OnLoad() has fully completed.
            // ContractOffered from new contract generation cannot fire until LockAcquire sets
            // generateContractIterations back to the default, which always happens after
            // LevelLoaded/TryGetContractLock. So there is no race between this and LockAcquire.
            System.StopIgnoringEvents();
            CreateUnavailableContractStubs();
        }

        /// <summary>
        /// After the ContractSystem finishes loading from the server snapshot, compares the set of
        /// contracts that actually loaded against the set that were expected. For each Offered
        /// contract that is absent — dropped by ContractConfigurator due to a missing mod type, or
        /// stripped pre-load due to a missing part — an <see cref="LmpUnavailableContract"/> stub
        /// is added to <see cref="ContractSystem.Instance"/> so the player can see which server
        /// contracts they cannot take on their client.
        /// </summary>
        private void CreateUnavailableContractStubs()
        {
            if (!System.Enabled) return;
            if (ContractSystem.Instance == null) return;

            var pending = System.PendingUnavailableContracts;
            if (pending.Count == 0) return;

            // Map GUID → contract object so we can inspect broken shells, not just presence.
            var loadedContracts = new Dictionary<string, Contract>();
            foreach (var contract in ContractSystem.Instance.Contracts)
            {
                if (contract != null && !(contract is LmpUnavailableContract))
                    loadedContracts[contract.ContractGuid.ToString()] = contract;
            }

            var stubsCreated = 0;
            foreach (var kvp in pending)
            {
                var guid = kvp.Key;
                loadedContracts.TryGetValue(guid, out var loaded);

                bool needsStub;
                if (loaded == null)
                {
                    // Contract was stripped pre-load or completely failed to produce an object.
                    needsStub = true;
                }
                else if (loaded.ParameterCount == 0)
                {
                    // Contract loaded as a parameterless shell — ContractConfigurator could not
                    // find the contract type (missing mod). CC's MeetRequirements() returns false
                    // for these, making them silently invisible. Replace with an informative stub.
                    ContractSystem.Instance.Contracts.Remove(loaded);
                    needsStub = true;
                }
                else
                {
                    needsStub = false;
                }

                if (!needsStub) continue;

                try
                {
                    var stub = BuildUnavailableContractStub(guid, kvp.Value.TypeName, kvp.Value.MissingAsset);
                    if (stub != null)
                    {
                        ContractSystem.Instance.Contracts.Add(stub);
                        stubsCreated++;
                        LunaLog.Log($"[ShareContracts]: Created unavailability stub for {guid} (type: {kvp.Value.TypeName}" +
                                    (kvp.Value.MissingAsset != null ? $", missing part: {kvp.Value.MissingAsset}" : string.Empty) + ").");
                    }
                }
                catch (Exception e)
                {
                    LunaLog.LogError($"[ShareContracts]: Failed to create unavailability stub for {guid}: {e.Message}");
                }
            }

            pending.Clear();

            if (stubsCreated > 0)
            {
                LunaLog.Log($"[ShareContracts]: {stubsCreated} unavailability stub(s) added to the Available contracts list.");
                GameEvents.Contract.onContractsListChanged.Fire();
            }
        }

        private static LmpUnavailableContract BuildUnavailableContractStub(string guid, string typeName, string missingAsset)
        {
            var node = new ConfigNode();
            node.AddValue("guid", guid);
            node.AddValue("prestige", "Trivial");
            node.AddValue("seed", "0");
            node.AddValue("state", "Offered");
            node.AddValue("viewed", "Unseen");
            node.AddValue("deadlineType", "None");
            node.AddValue("expiryType", "None");
            node.AddValue("ignoresWeight", "True");
            node.AddValue("values", "0,0,0,0,0,0,0,0,0,0,0,0");
            node.AddValue(LmpUnavailableContract.OriginalTypeKey, typeName);
            if (missingAsset != null)
                node.AddValue(LmpUnavailableContract.MissingAssetKey, missingAsset);

            return Contract.Load(new LmpUnavailableContract(), node) as LmpUnavailableContract;
        }

        public void ContractDeclined(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractMessage(contract);
            LunaLog.Log($"Contract declined: {contract.ContractGuid}");
        }

        public void ContractFailed(Contract contract)
        {
            if (System.IgnoreEvents) return;

            System.MessageSender.SendContractMessage(contract);
            LunaLog.Log($"Contract failed: {contract.ContractGuid}");
        }

        public void ContractFinished(Contract contract)
        {
            /*
            Doesn't need to be synchronized because there is no ContractFinished state.
            Also the contract will be finished on the contract complete / failed / cancelled / ...
            */
        }

        public void ContractOffered(Contract contract)
        {
            // LmpUnavailableContract stubs are injected by LMP itself — never touch them here.
            if (contract is LmpUnavailableContract) return;

            // Allow contracts being loaded from server data to pass through untouched.
            // IgnoreEvents is set both during ContractUpdate (ShareProgress path) and during
            // ContractSystem.OnLoad() (scenario restore path) via ContractSystem_OnLoad patch.
            if (System.IgnoreEvents) return;

            if (!LockSystem.LockQuery.ContractLockBelongsToPlayer(SettingsSystem.CurrentSettings.PlayerName))
            {
                //We don't have the contract lock, so discard any contract KSP generated locally.
                //New generation is already suppressed via generateContractIterations = 0; this
                //is a safety net for any edge case where KSP still fires the event.
                WithdrawAndRemoveContract(contract);
                return;
            }

            if (contract.GetType().Name == "RecoverAsset")
            {
                //We don't support rescue contracts. See: https://github.com/LunaMultiplayer/LunaMultiplayer/issues/226#issuecomment-431831526
                WithdrawAndRemoveContract(contract);
                return;
            }

            if (contract.GetType().Name == "TourismContract")
            {
                //We don't support tourism contracts.
                WithdrawAndRemoveContract(contract);
                return;
            }

            LunaLog.Log($"Contract offered: {contract.ContractGuid} - {contract.Title}");

            //This should be only called on the client with the contract lock, because it has the generationCount != 0.
            System.MessageSender.SendContractMessage(contract);
        }

        public void ContractParameterChanged(Contract contract, ContractParameter contractParameter)
        {
            //Do not send contract parameter changes as other players might override them
            //See: https://github.com/LunaMultiplayer/LunaMultiplayer/issues/186

            //TODO: Perhaps we can send only when the parameters are complete?
            //if (contractParameter.State == ParameterState.Complete)
            //    System.MessageSender.SendContractMessage(contract);

            LunaLog.Log($"Contract parameter changed on:{contract.ContractGuid}");
        }

        public void ContractRead(Contract contract)
        {
            LunaLog.Log($"Contract read:{contract.ContractGuid}");
        }

        public void ContractSeen(Contract contract)
        {
            LunaLog.Log($"Contract seen:{contract.ContractGuid}");
        }

        #endregion

        /// <summary>
        /// Withdraws a locally-generated contract and removes it from the ContractSystem without
        /// calling Contract.Kill(). Kill() destroys Unity GameObjects that ContractsApp's UIList
        /// may already hold references to, causing a NullReferenceException in UIList.Clear() the
        /// next time the contracts panel is opened. Withdraw() fires onContractsListChanged so the
        /// UI rebuilds cleanly while the entry is still alive, after which the contract is safely
        /// removed from memory.
        /// </summary>
        private static void WithdrawAndRemoveContract(Contract contract)
        {
            contract.Withdraw();
            ContractSystem.Instance.Contracts.Remove(contract);
            contract.Unregister();
        }
    }
}
