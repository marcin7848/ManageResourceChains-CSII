using Game;
using Game.Buildings;
using Game.Common;
using Game.Simulation;
using Game.Vehicles;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts service dispatches and cancels them if rules block the service.
    /// This runs AFTER pathfinding/dispatch and removes invalid ServiceDispatch entries.
    /// </summary>
    public partial class ServiceDispatchInterceptSystem : GameSystemBase
    {
        private ResourceChainRulesSystem m_RulesSystem;
        private EntityQuery m_PoliceCarQuery;
        private EntityQuery m_FireEngineQuery;
        private EntityQuery m_AmbulanceQuery;
        private EntityQuery m_HearseQuery;
        private EntityQuery m_GarbageTruckQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            m_RulesSystem = World.GetOrCreateSystemManaged<ResourceChainRulesSystem>();

            // Query for police cars with service dispatches
            m_PoliceCarQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<PoliceCar>(),
                    ComponentType.ReadWrite<ServiceDispatch>(),
                    ComponentType.ReadOnly<Owner>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>()
                }
            });

            // Similar queries for other services (fire, healthcare, etc.)
            // ...

            Mod.log.Info($"{nameof(ServiceDispatchInterceptSystem)} created - Will intercept service dispatches");
        }

        protected override void OnUpdate()
        {
            // Check police car dispatches
            if (!m_PoliceCarQuery.IsEmpty)
            {
                var policeEntities = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);
                var policeCarLookup = GetComponentLookup<PoliceCar>(false);
                var serviceDispatchLookup = GetBufferLookup<ServiceDispatch>(false);
                var ownerLookup = GetComponentLookup<Owner>(true);
                
                foreach (var vehicleEntity in policeEntities)
                {
                    if (!ownerLookup.TryGetComponent(vehicleEntity, out var owner))
                        continue;
                    
                    var policeStationEntity = owner.m_Owner;
                    
                    if (!serviceDispatchLookup.TryGetBuffer(vehicleEntity, out var dispatches))
                        continue;
                    
                    // Check each dispatch and remove if blocked by rules
                    for (int i = dispatches.Length - 1; i >= 0; i--)
                    {
                        var dispatch = dispatches[i];
                        
                        // Get the target of this dispatch (the request entity contains location info)
                        // We need to find what building this request is for
                        // This is complex - the request entity has PathInformation or other components
                        
                        // For now, log that we're checking it
                        Mod.log.Info($"Police car {vehicleEntity.Index} from station {policeStationEntity.Index} has dispatch for request {dispatch.m_Request.Index}");
                        
                        // TODO: Get target building from request and check rules
                        // bool allowed = m_RulesSystem.IsServiceTransportAllowed(policeStationEntity.Index, targetBuilding.Index);
                        // if (!allowed)
                        // {
                        //     Mod.log.Info($"  -> REMOVING dispatch - blocked by rules!");
                        //     dispatches.RemoveAt(i);
                        // }
                    }
                }
                
                policeEntities.Dispose();
            }
        }
    }
}
