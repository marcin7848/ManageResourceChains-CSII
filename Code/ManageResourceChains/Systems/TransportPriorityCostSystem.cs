using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Game;
using Game.Routes;
using ManageResourceChains.Data;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that applies transport priorities by modifying transport stop comfort factors
    /// to make prioritized transport more attractive to pathfinding
    /// </summary>
    public partial class TransportPriorityCostSystem : GameSystemBase
    {
        private EntityQuery _transportStopQuery;
        
        // Track which entities we've modified so we can restore them
        private Dictionary<Entity, TransportStopModification> _modifiedStops = new Dictionary<Entity, TransportStopModification>();
        
        // Store original values for restoration
        private struct TransportStopModification
        {
            public float OriginalComfortFactor;
            public float OriginalLoadingFactor;
        }
        
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Update every 256 frames to reduce performance impact
            return 256;
        }
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("TransportPriorityCostSystem created");
            
            // Query for all transport stops
            _transportStopQuery = GetEntityQuery(
                ComponentType.ReadWrite<TransportStop>()
            );
        }
        
        protected override void OnUpdate()
        {
            // Get all active configurations
            var allConfigs = ResourceChainManagementSystem.GetActiveConfigurations();
            
            // Collect all priority entities that should be modified
            var priorityEntities = new HashSet<int>();
            var priorityRules = new Dictionary<int, List<ResourceChainRule>>(); // Entity -> Rules that affect it
            
            foreach (var config in allConfigs.Values)
            {
                foreach (var rule in config.Rules)
                {
                    // Only process rules that have transport priorities and are for workers
                    // (Resources would need cargo stations which work differently)
                    if (rule.TransportPriorities != null && rule.TransportPriorities.Count > 0 
                        && rule.TransportType == Data.TransportType.Workers)
                    {
                        foreach (var priority in rule.TransportPriorities)
                        {
                            priorityEntities.Add(priority.StationEntity);
                            
                            if (!priorityRules.ContainsKey(priority.StationEntity))
                                priorityRules[priority.StationEntity] = new List<ResourceChainRule>();
                            
                            priorityRules[priority.StationEntity].Add(rule);
                        }
                    }
                }
            }
            
            // Apply modifications to priority entities
            ApplyPriorityModifications(priorityEntities, priorityRules);
            
            // Restore entities that are no longer priorities
            RestoreNonPriorityEntities(priorityEntities);
        }
        
        private void ApplyPriorityModifications(HashSet<int> priorityEntities, Dictionary<int, List<ResourceChainRule>> priorityRules)
        {
            foreach (var entityIndex in priorityEntities)
            {
                Entity entity = new Entity { Index = entityIndex, Version = 1 };
                
                // Check if entity still exists
                if (!EntityManager.Exists(entity))
                    continue;
                
                // Modify transport stops
                if (EntityManager.HasComponent<TransportStop>(entity))
                {
                    var stop = EntityManager.GetComponentData<TransportStop>(entity);
                    
                    // Store original values if this is the first time we're modifying this entity
                    if (!_modifiedStops.ContainsKey(entity))
                    {
                        _modifiedStops[entity] = new TransportStopModification
                        {
                            OriginalComfortFactor = stop.m_ComfortFactor,
                            OriginalLoadingFactor = stop.m_LoadingFactor
                        };
                        
                        Mod.log.Info($"Storing original values for stop {entityIndex}: Comfort={stop.m_ComfortFactor}, Loading={stop.m_LoadingFactor}");
                    }
                    
                    // Calculate priority boost based on number of rules affecting this stop
                    // and their priority values
                    float maxPriority = 0;
                    if (priorityRules.ContainsKey(entityIndex))
                    {
                        foreach (var rule in priorityRules[entityIndex])
                        {
                            var priority = rule.TransportPriorities.FirstOrDefault(p => p.StationEntity == entityIndex);
                            if (priority != null && priority.Priority > maxPriority)
                                maxPriority = priority.Priority;
                        }
                    }
                    
                    // Apply modifications
                    // High comfort factor = low penalty in pathfinding (1.0 = no penalty)
                    stop.m_ComfortFactor = UnityEngine.Mathf.Max(stop.m_ComfortFactor, 0.9f + (maxPriority / 100f));
                    stop.m_ComfortFactor = UnityEngine.Mathf.Min(stop.m_ComfortFactor, 1.0f); // Cap at 1.0
                    
                    // Increase loading factor slightly to reduce perceived wait time
                    stop.m_LoadingFactor = UnityEngine.Mathf.Max(stop.m_LoadingFactor, 0.8f + (maxPriority / 50f));
                    
                    EntityManager.SetComponentData(entity, stop);
                    Mod.log.Debug($"Applied priority to stop {entityIndex}: Comfort={stop.m_ComfortFactor}, Loading={stop.m_LoadingFactor}");
                }
            }
        }
        
        private void RestoreNonPriorityEntities(HashSet<int> currentPriorities)
        {
            // Find entities that were modified but are no longer priorities
            var toRestore = new List<Entity>();
            
            foreach (var kvp in _modifiedStops)
            {
                if (!currentPriorities.Contains(kvp.Key.Index))
                {
                    toRestore.Add(kvp.Key);
                }
            }
            
            // Restore original values
            foreach (var entity in toRestore)
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<TransportStop>(entity))
                {
                    var stop = EntityManager.GetComponentData<TransportStop>(entity);
                    var original = _modifiedStops[entity];
                    
                    stop.m_ComfortFactor = original.OriginalComfortFactor;
                    stop.m_LoadingFactor = original.OriginalLoadingFactor;
                    
                    EntityManager.SetComponentData(entity, stop);
                    Mod.log.Info($"Restored original values for stop {entity.Index}");
                }
                
                _modifiedStops.Remove(entity);
            }
        }
    }
}

