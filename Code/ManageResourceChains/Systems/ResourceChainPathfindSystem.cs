using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using ManageResourceChains.Data;
using Unity.Collections;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts pathfinding requests and applies resource chain rules
    /// to block or allow transport of workers, services, and resources between buildings.
    /// This system also enforces worker restrictions by removing workers from disallowed workplaces.
    /// </summary>
    public partial class ResourceChainPathfindSystem : GameSystemBase
    {
        private ResourceChainManagementSystem m_ResourceChainManagementSystem;
        private EntityQuery m_WorkerQuery;
        private EntityCommandBufferSystem m_EndFrameBarrier;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Update every 128 frames (about every half second at 60fps)
            // This balances responsiveness with performance
            return 128;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            // Query for all citizens who are workers
            m_WorkerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<Worker>(),
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<HouseholdMember>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>()
                }
            });

            Mod.log.Info($"{nameof(ResourceChainPathfindSystem)} created - Worker restriction enforcement enabled");
        }

        protected override void OnUpdate()
        {
            // Check existing workers and remove them from workplaces that violate rules
            EnforceWorkerRestrictions();
        }

        /// <summary>
        /// Get the district entity that a building belongs to (if any)
        /// Uses ComponentLookup to properly check for CurrentDistrict component
        /// </summary>
        private Entity GetBuildingDistrict(Entity building, ComponentLookup<CurrentDistrict> currentDistrictLookup)
        {
            // Check if entity exists and has CurrentDistrict component
            if (!EntityManager.Exists(building))
                return Entity.Null;

            if (currentDistrictLookup.HasComponent(building))
            {
                var currentDistrict = currentDistrictLookup[building];
                if (currentDistrict.m_District != Entity.Null && EntityManager.Exists(currentDistrict.m_District))
                {
                    return currentDistrict.m_District;
                }
            }

            return Entity.Null;
        }

        /// <summary>
        /// Actively enforces worker restrictions by removing workers from disallowed workplaces
        /// </summary>
        private void EnforceWorkerRestrictions()
        {
            var allConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            if (allConfigs == null || allConfigs.Count == 0)
                return; // No rules to enforce

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();

            // Get component lookups
            var workerLookup = GetComponentLookup<Worker>(false);
            var citizenLookup = GetComponentLookup<Citizen>(true);
            var householdMemberLookup = GetComponentLookup<HouseholdMember>(true);
            var propertyRenterLookup = GetComponentLookup<PropertyRenter>(true);
            var buildingLookup = GetComponentLookup<Building>(true);
            var employeeBufferLookup = GetBufferLookup<Employee>(false);
            var currentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);

            // Iterate through all workers
            var workers = m_WorkerQuery.ToEntityArray(Allocator.Temp);
            var workerComponents = m_WorkerQuery.ToComponentDataArray<Worker>(Allocator.Temp);
            var householdMembers = m_WorkerQuery.ToComponentDataArray<HouseholdMember>(Allocator.Temp);

            for (int i = 0; i < workers.Length; i++)
            {
                Entity citizenEntity = workers[i];
                Worker worker = workerComponents[i];
                Entity workplace = worker.m_Workplace;

                if (workplace == Entity.Null)
                    continue;

                // Get the citizen's home building (source of the worker)
                Entity household = householdMembers[i].m_Household;
                Entity homeBuilding = Entity.Null;

                if (propertyRenterLookup.HasComponent(household))
                {
                    homeBuilding = propertyRenterLookup[household].m_Property;
                }

                if (homeBuilding == Entity.Null)
                    continue; // Can't determine home, skip

                // Check if this worker is allowed to work at the workplace based on rules
                int homeId = homeBuilding.Index;
                int workplaceId = workplace.Index;

                bool isAllowed = IsWorkerTransportAllowed(homeId, workplaceId, TransportType.Workers, homeBuilding,
                    workplace, currentDistrictLookup);

                if (!isAllowed)
                {
                    // Remove worker from the workplace's employee list
                    if (employeeBufferLookup.HasBuffer(workplace))
                    {
                        var employees = employeeBufferLookup[workplace];
                        for (int j = 0; j < employees.Length; j++)
                        {
                            if (employees[j].m_Worker == citizenEntity)
                            {
                                employees.RemoveAt(j);
                                break;
                            }
                        }
                    }

                    // Remove Worker component from citizen
                    ecb.RemoveComponent<Worker>(citizenEntity);
                }
            }

            workers.Dispose();
            workerComponents.Dispose();
            householdMembers.Dispose();
        }

        /// <summary>
        /// Represents a rule with metadata about its source
        /// </summary>
        private class EvaluatedRule
        {
            public ResourceChainRule Rule { get; set; }
            public bool IsFromDistrict { get; set; }
            public int SourceEntityId { get; set; } // Building or District ID
        }

        /// <summary>
        /// Check if worker transport is allowed between a home and a workplace based on active rules.
        /// 
        /// Algorithm:
        /// 1. Collect all relevant rules from both buildings and their districts
        /// 2. Filter rules by direction (OUTGOING from home, INCOMING to workplace)
        /// 3. Remove district rules if building rules exist (building rules have priority)
        /// 4. Evaluate remaining rules to determine if transport is allowed
        /// 
        /// See WorkerTransportRules.md for detailed documentation.
        /// </summary>
        /// <param name="homeBuilding">Home building entity ID</param>
        /// <param name="workplaceBuilding">Workplace building entity ID</param>
        /// <param name="transportType">Type of transport (should be Workers)</param>
        /// <param name="homeEntity">Home building entity (for district lookup)</param>
        /// <param name="workplaceEntity">Workplace building entity (for district lookup)</param>
        /// <param name="currentDistrictLookup">ComponentLookup for CurrentDistrict</param>
        /// <returns>True if transport is allowed</returns>
        public bool IsWorkerTransportAllowed(int homeBuilding, int workplaceBuilding, TransportType transportType,
            Entity homeEntity, Entity workplaceEntity, ComponentLookup<CurrentDistrict> currentDistrictLookup)
        {
            if (transportType != TransportType.Workers)
                return true; // Only enforce worker restrictions for now

            // Get all building configurations
            var buildingConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            var districtConfigs = m_ResourceChainManagementSystem.GetAllDistrictConfigurations();

            if ((buildingConfigs == null || buildingConfigs.Count == 0) &&
                (districtConfigs == null || districtConfigs.Count == 0))
            {
                return true; // No rules configured, allow everything
            }

            // Get districts for both buildings
            Entity homeDistrict = GetBuildingDistrict(homeEntity, currentDistrictLookup);
            Entity workplaceDistrict = GetBuildingDistrict(workplaceEntity, currentDistrictLookup);

            // STEP 1: Collect all potentially relevant rules
            var allRules = new List<EvaluatedRule>();

            // Collect rules from home building
            if (buildingConfigs != null && buildingConfigs.TryGetValue(homeBuilding, out var homeConfig))
            {
                foreach (var rule in homeConfig.Rules)
                {
                    if (rule.TransportType == TransportType.Workers)
                    {
                        allRules.Add(new EvaluatedRule
                        {
                            Rule = rule,
                            IsFromDistrict = false,
                            SourceEntityId = homeBuilding
                        });
                    }
                }
            }

            // Collect rules from workplace building
            if (buildingConfigs != null && buildingConfigs.TryGetValue(workplaceBuilding, out var workplaceConfig))
            {
                foreach (var rule in workplaceConfig.Rules)
                {
                    if (rule.TransportType == TransportType.Workers)
                    {
                        allRules.Add(new EvaluatedRule
                        {
                            Rule = rule,
                            IsFromDistrict = false,
                            SourceEntityId = workplaceBuilding
                        });
                    }
                }
            }

            // Collect rules from home district
            if (homeDistrict != Entity.Null && districtConfigs != null &&
                districtConfigs.TryGetValue(homeDistrict.Index, out var homeDistrictConfig))
            {
                foreach (var rule in homeDistrictConfig.Rules)
                {
                    if (rule.TransportType == TransportType.Workers)
                    {
                        allRules.Add(new EvaluatedRule
                        {
                            Rule = rule,
                            IsFromDistrict = true,
                            SourceEntityId = homeDistrict.Index
                        });
                    }
                }
            }

            // Collect rules from workplace district
            if (workplaceDistrict != Entity.Null && districtConfigs != null &&
                districtConfigs.TryGetValue(workplaceDistrict.Index, out var workplaceDistrictConfig))
            {
                foreach (var rule in workplaceDistrictConfig.Rules)
                {
                    if (rule.TransportType == TransportType.Workers)
                    {
                        allRules.Add(new EvaluatedRule
                        {
                            Rule = rule,
                            IsFromDistrict = true,
                            SourceEntityId = workplaceDistrict.Index
                        });
                    }
                }
            }

            // If no rules found, allow transport
            if (allRules.Count == 0)
                return true;

            // STEP 2: Filter by direction - keep only OUTGOING from home and INCOMING to workplace
            var directionFilteredRules = new List<EvaluatedRule>();

            foreach (var evaluatedRule in allRules)
            {
                bool isFromHome = evaluatedRule.SourceEntityId == homeBuilding ||
                                  (homeDistrict != Entity.Null && evaluatedRule.SourceEntityId == homeDistrict.Index &&
                                   evaluatedRule.IsFromDistrict);

                bool isFromWorkplace = evaluatedRule.SourceEntityId == workplaceBuilding ||
                                       (workplaceDistrict != Entity.Null &&
                                        evaluatedRule.SourceEntityId == workplaceDistrict.Index &&
                                        evaluatedRule.IsFromDistrict);

                // Keep OUTGOING rules from home or its district
                if (isFromHome && evaluatedRule.Rule.Type == ChainType.Outgoing)
                {
                    directionFilteredRules.Add(evaluatedRule);
                }
                // Keep INCOMING rules to workplace or its district
                else if (isFromWorkplace && evaluatedRule.Rule.Type == ChainType.Incoming)
                {
                    directionFilteredRules.Add(evaluatedRule);
                }
            }

            // If no rules match the direction, allow transport
            if (directionFilteredRules.Count == 0)
                return true;

            // STEP 3: Remove district rules if building rules exist (building rules have priority)
            bool hasBuildingRules = directionFilteredRules.Any(r => !r.IsFromDistrict);

            var finalRules = hasBuildingRules
                ? directionFilteredRules.Where(r => !r.IsFromDistrict).ToList()
                : directionFilteredRules;

            // If after filtering no rules remain, allow transport
            if (finalRules.Count == 0)
                return true;

            // STEP 4: Evaluate remaining rules
            return EvaluateRules(finalRules, homeBuilding, workplaceBuilding, homeDistrict, workplaceDistrict);
        }

        /// <summary>
        /// Evaluates a list of rules to determine if transport is allowed.
        /// 
        /// Logic:
        /// - ALLOW rules act as a whitelist: transport is allowed ONLY if target matches
        /// - DISALLOW rules act as a blacklist: transport is blocked if target matches
        /// - If any DISALLOW rule matches, transport is blocked
        /// - If any ALLOW rule exists but none match, transport is blocked
        /// - If only DISALLOW rules exist and none match, transport is allowed
        /// </summary>
        private bool EvaluateRules(List<EvaluatedRule> rules, int homeBuilding, int workplaceBuilding,
            Entity homeDistrict, Entity workplaceDistrict)
        {
            bool hasAllowRules = false;
            bool hasMatchingAllowRule = false;
            bool hasMatchingDisallowRule = false;

            foreach (var evaluatedRule in rules)
            {
                var rule = evaluatedRule.Rule;

                // Determine the target building and district based on rule direction
                int targetBuilding;
                Entity targetDistrict;

                if (rule.Type == ChainType.Outgoing)
                {
                    // OUTGOING from home -> check if workplace is in the rule's target list
                    targetBuilding = workplaceBuilding;
                    targetDistrict = workplaceDistrict;
                }
                else // ChainType.Incoming
                {
                    // INCOMING to workplace -> check if home is in the rule's target list
                    targetBuilding = homeBuilding;
                    targetDistrict = homeDistrict;
                }

                // Check if target matches the rule
                bool isInList = rule.Buildings.Contains(targetBuilding) ||
                                (targetDistrict != Entity.Null && rule.Districts.Contains(targetDistrict.Index));

                if (rule.Allow == AllowType.Allow)
                {
                    hasAllowRules = true;
                    if (isInList)
                    {
                        hasMatchingAllowRule = true;
                    }
                }
                else // AllowType.Disallow
                {
                    if (isInList)
                    {
                        hasMatchingDisallowRule = true;
                    }
                }
            }

            // If any DISALLOW rule matches, block transport
            if (hasMatchingDisallowRule)
                return false;

            // If there are ALLOW rules but none match, block transport
            if (hasAllowRules && !hasMatchingAllowRule)
                return false;

            // Otherwise, allow transport
            return true;
        }
    }
}