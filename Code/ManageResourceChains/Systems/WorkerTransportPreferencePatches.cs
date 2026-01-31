using System;
using System.Reflection;
using HarmonyLib;
using Game.Citizens;
using Game.Pathfind;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// Harmony patches for modifying individual worker pathfinding based on building/district preferences.
    /// 
    /// Unlike the global TransportPreferencePatches, this system applies preferences per-worker
    /// based on their home and workplace buildings/districts.
    /// 
    /// The key insight: We intercept CitizenUtils.GetPathfindWeights and check if the citizen
    /// has a transport preference stored in TransportPreferenceSystem. If so, we modify the weights
    /// to heavily favor that transport type.
    /// </summary>
    public static class WorkerTransportPreferencePatches
    {
        private static Harmony _harmony;
        private static bool _patchesApplied = false;
        private static TransportPreferenceSystem _transportPreferenceSystem;
        
        public static void ApplyPatches(TransportPreferenceSystem system)
        {
            if (_patchesApplied)
                return;
                
            _transportPreferenceSystem = system;
                
            try
            {
                _harmony = new Harmony("ManageResourceChains.WorkerTransportPreference");
                
                // Patch CitizenUtils.GetPathfindWeights to modify weights for specific workers
                var weightsMethod = typeof(CitizenUtils).GetMethod(
                    "GetPathfindWeights", 
                    BindingFlags.Public | BindingFlags.Static);
                    
                if (weightsMethod != null)
                {
                    var postfixMethod = typeof(WorkerTransportPreferencePatches).GetMethod(
                        nameof(GetPathfindWeights_Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic);
                        
                    _harmony.Patch(weightsMethod, postfix: new HarmonyMethod(postfixMethod));
                    Mod.log.Info("[WorkerTransportPreferencePatches] Successfully patched CitizenUtils.GetPathfindWeights");
                }
                else
                {
                    Mod.log.Warn("[WorkerTransportPreferencePatches] Could not find CitizenUtils.GetPathfindWeights method");
                }
                
                _patchesApplied = true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"[WorkerTransportPreferencePatches] Error applying patches: {ex.Message}");
                Mod.log.Error(ex.StackTrace);
            }
        }
        
        public static void RemovePatches()
        {
            if (!_patchesApplied)
                return;
                
            try
            {
                _harmony?.UnpatchAll("ManageResourceChains.WorkerTransportPreference");
                _patchesApplied = false;
                _transportPreferenceSystem = null;
                Mod.log.Info("[WorkerTransportPreferencePatches] Successfully removed patches");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"[WorkerTransportPreferencePatches] Error removing patches: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Postfix patch for CitizenUtils.GetPathfindWeights
        /// 
        /// Checks if this citizen has a transport preference and modifies weights accordingly.
        /// The modification makes the preferred transport type extremely cheap in pathfinding cost.
        /// </summary>
        private static void GetPathfindWeights_Postfix(ref PathfindWeights __result, Citizen citizen, Household household, int householdCitizens, Entity citizenEntity)
        {
            if (_transportPreferenceSystem == null)
                return;
                
            // Check if this citizen has a transport preference
            var preference = _transportPreferenceSystem.GetWorkerPreference(citizenEntity);
            
            if (preference == TransportPreferenceSystem.PreferredTransport.None)
                return;
            
            // Apply preference weight modifications
            __result = TransportPreferenceSystem.ApplyPreferenceWeights(__result, preference);
            
            // Log occasionally for debugging (every 200th call to avoid spam)
            if (UnityEngine.Random.Range(0, 200) == 0)
            {
                Mod.log.Info($"[WorkerTransportPreferencePatches] Applied {preference} preference weights to citizen {citizenEntity.Index}");
            }
        }
    }
}
