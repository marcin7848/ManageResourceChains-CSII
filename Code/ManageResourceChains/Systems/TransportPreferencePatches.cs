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
    /// Harmony patches for modifying citizen pathfinding to FORCE public transport usage.
    /// 
    /// The key insight: Citizens choose transport based on PathfindParameters.m_Methods flags.
    /// If we remove Road/Parking/Taxi/Bicycle methods, citizens MUST use public transport or walk.
    /// 
    /// We patch:
    /// 1. CitizenUtils.GetPathfindWeights - to make public transport cost-effective
    /// 2. The pathfinding setup to remove car/taxi/bicycle methods when bus preference is enabled
    /// </summary>
    public static class TransportPreferencePatches
    {
        private static Harmony _harmony;
        
        // Track if patches are applied
        private static bool _patchesApplied = false;
        
        public static void ApplyPatches()
        {
            if (_patchesApplied)
                return;
                
            try
            {
                _harmony = new Harmony("ManageResourceChains.TransportPreference");
                
                // Patch 1: CitizenUtils.GetPathfindWeights - modify weights to favor public transport
                var weightsMethod = typeof(CitizenUtils).GetMethod(
                    "GetPathfindWeights", 
                    BindingFlags.Public | BindingFlags.Static);
                    
                if (weightsMethod != null)
                {
                    var postfixMethod = typeof(TransportPreferencePatches).GetMethod(
                        nameof(GetPathfindWeights_Postfix),
                        BindingFlags.Static | BindingFlags.NonPublic);
                        
                    _harmony.Patch(weightsMethod, postfix: new HarmonyMethod(postfixMethod));
                }
                
                _patchesApplied = true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error applying transport preference patches: {ex.Message}");
                Mod.log.Error(ex.StackTrace);
            }
        }
        
        public static void RemovePatches()
        {
            if (!_patchesApplied)
                return;
                
            try
            {
                _harmony?.UnpatchAll("ManageResourceChains.TransportPreference");
                _patchesApplied = false;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error removing transport preference patches: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Postfix patch for CitizenUtils.GetPathfindWeights
        /// 
        /// When preference is enabled, we DRASTICALLY modify weights to make
        /// the preferred transport extremely cheap in the cost calculation.
        /// 
        /// Weight components: (time, behaviour, money, comfort)
        /// - time: how much we care about travel time
        /// - behaviour: traffic rules, etc.
        /// - money: ticket cost, fuel cost
        /// - comfort: crowding, walking distance, etc.
        /// 
        /// For public transport preference:
        /// - Make time weight very low (we don't care if transport takes longer)
        /// - Make money weight VERY HIGH (makes expensive transport prohibitive)
        /// - Make comfort weight very low (we accept transport discomfort)
        /// </summary>
        private static void GetPathfindWeights_Postfix(ref PathfindWeights __result, Citizen citizen, Household household, int householdCitizens)
        {
            var preference = TransportPreferenceSystem.DefaultPreference;
            
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.None)
                return;
            
            float4 weights = __result.m_Value;
            
            // Public transport types (bus, train, tram, metro, ferry, airplane)
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.Bus ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Train ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Tram ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Metro ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Ferry ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Airplane)
            {
                // AGGRESSIVE public transport preference settings:
                // 
                // The trick: Cars have LOW money cost but HIGH time efficiency.
                // Public transport has HIGHER money cost (ticket) but LOWER time efficiency.
                // 
                // By making time weight VERY LOW and money weight EXTREMELY HIGH,
                // the preferred transport's lower money cost outweighs alternatives.
                
                // Time weight: 0.001 (completely ignore time)
                weights.x = 0.001f;
                
                // Behaviour weight: keep normal
                // weights.y unchanged
                
                // Money weight: EXTREMELY HIGH (makes expensive transport completely prohibitive)
                // With preferred ticket = 0 and non-preferred ticket = 65535, and money weight = 1000,
                // the non-preferred cost is 65,535,000 which is astronomical
                weights.z = 1000f;
                
                // Comfort weight: 0.001 (completely ignore comfort)
                weights.w = 0.001f;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Taxi)
            {
                // Taxi preference - don't care about cost
                weights.z = 0.01f;
                weights.w *= 0.5f;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Walking)
            {
                // Walking preference - time matters less
                weights.x = 0.01f;
                weights.z = 0.01f;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Bicycle)
            {
                // Bicycle preference
                weights.x *= 0.3f;
                weights.z *= 0.1f;
                weights.w *= 0.5f;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Car)
            {
                // Car preference - don't care about parking/fuel cost
                weights.x *= 0.5f; // Time still matters somewhat
                weights.z *= 0.1f; // Don't care about cost
                weights.w *= 0.5f;
            }
            
            __result = new PathfindWeights(weights.x, weights.y, weights.z, weights.w);
        }
        
        /// <summary>
        /// Helper method to get restricted path methods based on preference.
        /// This removes unwanted transport methods from available options.
        /// </summary>
        public static PathMethod GetRestrictedMethods(PathMethod originalMethods)
        {
            var preference = TransportPreferenceSystem.DefaultPreference;
            
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.None)
                return originalMethods;
            
            // Public transport preferences
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.Bus ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Train ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Tram ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Metro ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Ferry ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.Airplane)
            {
                // Remove car-related methods
                PathMethod restricted = originalMethods;
                restricted &= ~PathMethod.Road;           // No driving
                restricted &= ~PathMethod.Parking;        // No parking
                restricted &= ~PathMethod.Taxi;           // No taxis
                restricted &= ~PathMethod.Bicycle;        // No bicycles
                restricted &= ~PathMethod.BicycleParking; // No bike parking
                restricted &= ~PathMethod.MediumRoad;     // No medium roads (trucks)
                
                // Ensure pedestrian and public transport are always available
                restricted |= PathMethod.Pedestrian;
                restricted |= PathMethod.PublicTransportDay;
                restricted |= PathMethod.PublicTransportNight;
                restricted |= PathMethod.Boarding;
                
                return restricted;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Taxi)
            {
                // Remove everything except taxi and pedestrian
                PathMethod restricted = PathMethod.Pedestrian | PathMethod.Taxi;
                return restricted;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Walking)
            {
                // Only pedestrian
                return PathMethod.Pedestrian;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Bicycle)
            {
                // Only bicycle and pedestrian
                return PathMethod.Pedestrian | PathMethod.Bicycle | PathMethod.BicycleParking;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Car)
            {
                // Only car and pedestrian
                return PathMethod.Pedestrian | PathMethod.Road | PathMethod.Parking;
            }
            
            return originalMethods;
        }
    }
}
