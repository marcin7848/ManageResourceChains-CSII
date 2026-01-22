using Unity.Entities;
using Unity.Mathematics;
using Game;
using Game.Simulation;
using System.Collections.Generic;
using System.Linq;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that updates the global transport preference based on all active worker configurations.
    /// This runs periodically and sets TransportPreferenceSystem.DefaultPreference to the most common
    /// preference across all configured buildings.
    /// 
    /// If multiple transport types are configured, it randomly picks one of the most common ones.
    /// </summary>
    public partial class WorkerTransportPreferenceSystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private Random m_Random;
        private uint m_LastUpdateFrame = 0;
        private const uint UPDATE_INTERVAL = 60; // Update every 60 frames (~1 second) - much faster response
        
        // Public flag to force immediate update (set by configuration save)
        public static bool ForceUpdate = false;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Random = new Random((uint)System.DateTime.Now.Ticks);
        }
        
        protected override void OnUpdate()
        {
            uint currentFrame = m_SimulationSystem.frameIndex;
            
            // Check if we should update (either periodic or forced)
            bool shouldUpdate = ForceUpdate || (currentFrame - m_LastUpdateFrame >= UPDATE_INTERVAL);
            
            if (!shouldUpdate)
                return;
            
            // Reset force flag and update timestamp
            ForceUpdate = false;
            m_LastUpdateFrame = currentFrame;
            
            Mod.log.Info("=== WorkerTransportPreferenceSystem Update ===");
            
            // Get all active building configurations
            var allConfigs = ResourceChainManagementSystem.GetActiveConfigurations();
            
            Mod.log.Info($"Found {allConfigs?.Count ?? 0} active configurations");
            
            if (allConfigs == null || allConfigs.Count == 0)
            {
                // No configurations - use None
                Mod.log.Info("No configurations found, setting DefaultPreference to None");
                TransportPreferenceSystem.DefaultPreference = TransportPreferenceSystem.PreferredTransportMethod.None;
                return;
            }
            
            // Collect all enabled transport preferences from all worker rules
            var transportCounts = new Dictionary<TransportPreferenceSystem.PreferredTransportMethod, int>();
            
            foreach (var configEntry in allConfigs.Values)
            {
                if (configEntry.Rules == null)
                    continue;
                
                Mod.log.Info($"Processing config with {configEntry.Rules.Count} rules");
                
                foreach (var rule in configEntry.Rules)
                {
                    // Only consider rules that affect workers
                    if (rule.TransportType != Data.TransportType.Workers)
                    {
                        Mod.log.Info($"Skipping rule - TransportType is {rule.TransportType}, not Workers");
                        continue;
                    }
                    
                    // IMPORTANT: Only count rules that have buildings or districts assigned!
                    // Empty rules shouldn't influence the global preference
                    bool hasBuildings = rule.Buildings != null && rule.Buildings.Count > 0;
                    bool hasDistricts = rule.Districts != null && rule.Districts.Count > 0;
                    
                    if (!hasBuildings && !hasDistricts)
                    {
                        Mod.log.Info("Skipping rule - no buildings or districts assigned");
                        continue;
                    }
                    
                    var prefs = rule.TransportPreferences;
                    if (prefs == null)
                    {
                        Mod.log.Info("Skipping rule - TransportPreferences is null");
                        continue;
                    }
                    
                    Mod.log.Info($"Worker rule found - Bus:{prefs.Bus} Train:{prefs.Train} Tram:{prefs.Tram} Metro:{prefs.Metro} Ferry:{prefs.Ferry} Airplane:{prefs.Airplane} Taxi:{prefs.Taxi} Walking:{prefs.Walking} Bicycle:{prefs.Bicycle} Car:{prefs.Car}");
                    
                    // Count each enabled transport type
                    if (prefs.Bus) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Bus);
                    if (prefs.Train) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Train);
                    if (prefs.Tram) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Tram);
                    if (prefs.Metro) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Metro);
                    if (prefs.Ferry) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Ferry);
                    if (prefs.Airplane) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Airplane);
                    if (prefs.Taxi) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Taxi);
                    if (prefs.Walking) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Walking);
                    if (prefs.Bicycle) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Bicycle);
                    if (prefs.Car) IncrementCount(transportCounts, TransportPreferenceSystem.PreferredTransportMethod.Car);
                }
            }
            
            // If no transports are enabled, use None
            if (transportCounts.Count == 0)
            {
                Mod.log.Info("No transport preferences enabled, setting DefaultPreference to None");
                TransportPreferenceSystem.DefaultPreference = TransportPreferenceSystem.PreferredTransportMethod.None;
                return;
            }
            
            // Log counts
            foreach (var kvp in transportCounts)
            {
                Mod.log.Info($"Transport count: {kvp.Key} = {kvp.Value}");
            }
            
            // Find all transports with the maximum count
            int maxCount = transportCounts.Values.Max();
            var mostCommonTransports = transportCounts.Where(kvp => kvp.Value == maxCount)
                                                      .Select(kvp => kvp.Key)
                                                      .ToList();
            
            // If multiple transports have the same max count, randomly pick one
            TransportPreferenceSystem.PreferredTransportMethod selectedTransport;
            if (mostCommonTransports.Count == 1)
            {
                selectedTransport = mostCommonTransports[0];
            }
            else
            {
                int randomIndex = m_Random.NextInt(mostCommonTransports.Count);
                selectedTransport = mostCommonTransports[randomIndex];
                Mod.log.Info($"Multiple transports tied with {maxCount} votes, randomly selected {selectedTransport}");
            }
            
            // Update the global preference
            Mod.log.Info($"Setting DefaultPreference to {selectedTransport}");
            TransportPreferenceSystem.DefaultPreference = selectedTransport;
        }
        
        private void IncrementCount(Dictionary<TransportPreferenceSystem.PreferredTransportMethod, int> dict, 
                                     TransportPreferenceSystem.PreferredTransportMethod method)
        {
            if (dict.ContainsKey(method))
                dict[method]++;
            else
                dict[method] = 1;
        }
    }
}
