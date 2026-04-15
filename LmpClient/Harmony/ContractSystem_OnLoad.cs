using Contracts;
using HarmonyLib;
using LmpClient.Systems.ShareContracts;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Keeps IgnoreEvents = true for the entire ContractSystem.OnLoad() window so that contracts
    /// restored from the server scenario data are not killed by the ContractOffered
    /// lock-ownership check.
    ///
    /// ContractSystem does NOT declare its own OnLoad — the method lives on ScenarioModule.
    /// HarmonyPatch attribute lookup only searches the declared methods of the specified type,
    /// so [HarmonyPatch(typeof(ContractSystem), "OnLoad")] silently finds nothing and skips.
    /// The correct target is ScenarioModule, which is the declaring type. We guard on
    /// __instance type so only the ContractSystem load is affected.
    ///
    /// IgnoreEvents is intentionally NOT cleared here. ShareContractsEvents.ContractsLoaded()
    /// (triggered by onContractsLoaded, which fires after OnLoad completes) is the correct
    /// point to stop ignoring events. Clearing it here — before onContractsLoaded fires —
    /// creates a window in which mods such as Contract Configurator may fire onOffered for the
    /// newly-loaded contracts; LMP's ContractOffered handler would then withdraw every server
    /// contract the player does not hold the lock for, wiping the Available tab.
    /// </summary>
    [HarmonyPatch(typeof(ScenarioModule))]
    [HarmonyPatch("OnLoad")]
    public class ContractSystem_OnLoad
    {
        [HarmonyPrefix]
        private static void PrefixOnLoad(ScenarioModule __instance)
        {
            if (!(__instance is ContractSystem)) return;
            if (MainSystem.NetworkState < ClientState.Connected) return;

            var system = ShareContractsSystem.Singleton;
            if (system?.Enabled != true) return;

            // Safety net: IgnoreEvents should already be true (set in ShareContractsSystem.OnEnabled),
            // but call StartIgnoringEvents defensively in case something cleared it prematurely.
            system.StartIgnoringEvents();
        }
    }
}
