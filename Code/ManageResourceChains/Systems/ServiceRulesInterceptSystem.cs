using Game;
using Game.Buildings;
using Game.Common;
using Game.Pathfind;
using Game.Simulation;
using Game.Tools;
using ManageResourceChains.Data;
using Unity.Entities;
using Unity.Collections;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts service pathfinding requests and applies service rules
    /// to block or allow service vehicles from responding to specific buildings.
    /// 
    /// This system modifies PathfindTarget requests to filter out buildings that
    /// are not allowed by the configured rules.
    /// </summary>
    public partial class ServiceRulesInterceptSystem : GameSystemBase
    {
        private ResourceChainRulesSystem m_RulesSystem;
        private EntityQuery m_PoliceStationQuery;
        private EntityQuery m_HospitalQuery;
        private EntityQuery m_FireStationQuery;
        private EntityQuery m_GarbageFacilityQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            m_RulesSystem = World.GetOrCreateSystemManaged<ResourceChainRulesSystem>();

            // Query for police stations
            m_PoliceStationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PoliceStation>(),
                    ComponentType.ReadOnly<Building>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            // Query for hospitals
            m_HospitalQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Hospital>(),
                    ComponentType.ReadOnly<Building>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            // Query for fire stations
            m_FireStationQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<FireStation>(),
                    ComponentType.ReadOnly<Building>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            // Query for garbage facilities
            m_GarbageFacilityQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<GarbageFacility>(),
                    ComponentType.ReadOnly<Building>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            // Apply Harmony patches now that systems are created
            var harmony = ManageResourceChains.Mod.HarmonyInstance;
            if (harmony != null)
            {
                ServicePathfindingPatches.Apply(harmony, this, m_RulesSystem);
                Mod.log.Info($"{nameof(ServiceRulesInterceptSystem)} created and Harmony patches applied");
            }
            else
            {
                Mod.log.Warn($"{nameof(ServiceRulesInterceptSystem)} created but Harmony not available");
            }
        }

        protected override void OnUpdate()
        {
            // This system validates service rules
            // It is called when checking if a service can respond to a target
        }

        /// <summary>
        /// Checks if a healthcare service (ambulance/hearse) can serve a target building
        /// </summary>
        /// <param name="hospitalEntity">The hospital entity providing service</param>
        /// <param name="targetEntity">The target building/citizen location</param>
        /// <returns>True if service is allowed</returns>
        public bool CheckHealthcareService(Entity hospitalEntity, Entity targetEntity)
        {
            // Skip if entities are invalid
            if (hospitalEntity == Entity.Null || targetEntity == Entity.Null)
                return true;

            int hospitalId = hospitalEntity.Index;
            int targetId = targetEntity.Index;

            return m_RulesSystem.IsServiceTransportAllowed(hospitalId, targetId);
        }

        /// <summary>
        /// Checks if a fire service can respond to a target building
        /// </summary>
        /// <param name="fireStationEntity">The fire station entity</param>
        /// <param name="targetEntity">The building on fire</param>
        /// <returns>True if service is allowed</returns>
        public bool CheckFireService(Entity fireStationEntity, Entity targetEntity)
        {
            if (fireStationEntity == Entity.Null || targetEntity == Entity.Null)
                return true;

            int stationId = fireStationEntity.Index;
            int targetId = targetEntity.Index;

            return m_RulesSystem.IsServiceTransportAllowed(stationId, targetId);
        }

        /// <summary>
        /// Checks if a police service can respond to a target building
        /// </summary>
        /// <param name="policeStationEntity">The police station entity</param>
        /// <param name="targetEntity">The target building</param>
        /// <returns>True if service is allowed</returns>
        public bool CheckPoliceService(Entity policeStationEntity, Entity targetEntity)
        {
            if (policeStationEntity == Entity.Null || targetEntity == Entity.Null)
                return true;

            int stationId = policeStationEntity.Index;
            int targetId = targetEntity.Index;

            return m_RulesSystem.IsServiceTransportAllowed(stationId, targetId);
        }

        /// <summary>
        /// Checks if a garbage service can collect from a target building
        /// </summary>
        /// <param name="garbageFacilityEntity">The garbage facility entity</param>
        /// <param name="targetEntity">The building with garbage</param>
        /// <returns>True if service is allowed</returns>
        public bool CheckGarbageService(Entity garbageFacilityEntity, Entity targetEntity)
        {
            if (garbageFacilityEntity == Entity.Null || targetEntity == Entity.Null)
                return true;

            int facilityId = garbageFacilityEntity.Index;
            int targetId = targetEntity.Index;

            return m_RulesSystem.IsServiceTransportAllowed(facilityId, targetId);
        }

        /// <summary>
        /// Checks if a post service can deliver to a target building
        /// </summary>
        /// <param name="postOfficeEntity">The post office entity</param>
        /// <param name="targetEntity">The target building</param>
        /// <returns>True if service is allowed</returns>
        public bool CheckPostService(Entity postOfficeEntity, Entity targetEntity)
        {
            if (postOfficeEntity == Entity.Null || targetEntity == Entity.Null)
                return true;

            int officeId = postOfficeEntity.Index;
            int targetId = targetEntity.Index;

            return m_RulesSystem.IsServiceTransportAllowed(officeId, targetId);
        }
    }
}
