using Unity.Entities;
using Unity.Collections;
using Game;
using Game.Common;
using ManageResourceChains.Data;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// DEPRECATED: This system has been replaced by TransportPreferenceSystem and TransportPreferencePatches.
    /// 
    /// The old approach tried to intercept worker commutes and force them via specific bus stops.
    /// This was complex and error-prone, requiring manual path element manipulation.
    /// 
    /// The new approach (TransportPreferenceSystem) works by:
    /// 1. Patching CitizenUtils.GetPathfindWeights to modify pathfinding weights
    /// 2. This makes public transport dramatically cheaper in the pathfinding cost calculation
    /// 3. The game's native AI then naturally chooses public transport
    /// 
    /// This stub system remains to clean up any ForcedPriorityTrip components from old saves.
    /// The ForcedPriorityTrip component definition remains in ResourceChainConfig.cs for save compatibility.
    /// </summary>
    public partial class WorkerTransportPrioritySystem : GameSystemBase
    {
        private EntityQuery m_CleanupQuery;
        private bool m_HasCleanedUp = false;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("WorkerTransportPrioritySystem created (STUB - functionality moved to TransportPreferenceSystem)");
            
            // Query for any entities with the old ForcedPriorityTrip component
            m_CleanupQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ForcedPriorityTrip>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            // One-time cleanup of old ForcedPriorityTrip components
            if (!m_HasCleanedUp && !m_CleanupQuery.IsEmpty)
            {
                Mod.log.Info("Cleaning up old ForcedPriorityTrip components from previous save...");
                
                var entities = m_CleanupQuery.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    if (EntityManager.Exists(entity))
                    {
                        EntityManager.RemoveComponent<ForcedPriorityTrip>(entity);
                    }
                }
                entities.Dispose();
                
                Mod.log.Info($"Cleaned up ForcedPriorityTrip from {entities.Length} entities");
                m_HasCleanedUp = true;
            }
            
            // Disable after cleanup - this system doesn't need to run anymore
            if (m_HasCleanedUp || m_CleanupQuery.IsEmpty)
            {
                Enabled = false;
            }
        }
    }
}
