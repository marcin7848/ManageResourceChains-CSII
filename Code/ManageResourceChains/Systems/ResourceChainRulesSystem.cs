using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using ManageResourceChains.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts pathfinding requests and applies resource chain rules
    /// to block or allow transport of workers, services, and resources between buildings.
    /// This system also enforces worker restrictions by removing workers from disallowed workplaces.
    /// </summary>
    public partial class ResourceChainRulesSystem : GameSystemBase
    {
        /// <summary>
        /// Burst-compiled job that checks workers in parallel and marks invalid ones for removal
        /// </summary>
        [BurstCompile]
        private struct CheckWorkerRestrictionsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Worker> WorkerType;
            [ReadOnly] public ComponentTypeHandle<HouseholdMember> HouseholdMemberType;
            [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenterLookup;
            [ReadOnly] public ComponentLookup<CurrentDistrict> CurrentDistrictLookup;
            [ReadOnly] public NativeHashMap<int, ConfigData> BuildingConfigs;
            [ReadOnly] public NativeHashMap<int, ConfigData> DistrictConfigs;
            
            public NativeQueue<WorkerRemovalData>.ParallelWriter WorkersToRemove;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var workers = chunk.GetNativeArray(ref WorkerType);
                var householdMembers = chunk.GetNativeArray(ref HouseholdMemberType);

                for (int i = 0; i < entities.Length; i++)
                {
                    Entity citizenEntity = entities[i];
                    Worker worker = workers[i];
                    Entity workplace = worker.m_Workplace;

                    if (workplace == Entity.Null)
                        continue;

                    // Get the citizen's home building
                    Entity household = householdMembers[i].m_Household;
                    Entity homeBuilding = Entity.Null;

                    if (PropertyRenterLookup.HasComponent(household))
                    {
                        homeBuilding = PropertyRenterLookup[household].m_Property;
                    }

                    if (homeBuilding == Entity.Null)
                        continue;

                    // Check if this worker is allowed
                    int homeId = homeBuilding.Index;
                    int workplaceId = workplace.Index;

                    bool isAllowed = IsWorkerTransportAllowedJob(homeId, workplaceId, homeBuilding, workplace,
                        CurrentDistrictLookup, BuildingConfigs, DistrictConfigs);

                    if (!isAllowed)
                    {
                        WorkersToRemove.Enqueue(new WorkerRemovalData
                        {
                            CitizenEntity = citizenEntity,
                            WorkplaceEntity = workplace
                        });
                    }
                }
            }

            /// <summary>
            /// Job-compatible version of IsWorkerTransportAllowed that uses NativeHashMaps
            /// </summary>
            private static bool IsWorkerTransportAllowedJob(int homeBuilding, int workplaceBuilding,
                Entity homeEntity, Entity workplaceEntity,
                ComponentLookup<CurrentDistrict> currentDistrictLookup,
                NativeHashMap<int, ConfigData> buildingConfigs,
                NativeHashMap<int, ConfigData> districtConfigs)
            {
                if (buildingConfigs.Count == 0 && districtConfigs.Count == 0)
                    return true;

                // Get districts
                Entity homeDistrict = GetBuildingDistrictJob(homeEntity, currentDistrictLookup);
                Entity workplaceDistrict = GetBuildingDistrictJob(workplaceEntity, currentDistrictLookup);

                // Collect relevant rules
                var relevantRules = new NativeList<RuleData>(8, Allocator.Temp);

                // Collect from home building
                if (buildingConfigs.TryGetValue(homeBuilding, out var homeConfig))
                {
                    for (int i = 0; i < homeConfig.Rules.Length; i++)
                    {
                        var rule = homeConfig.Rules[i];
                        if (rule.TransportType == (byte)TransportType.Workers)
                        {
                            relevantRules.Add(new RuleData
                            {
                                Rule = rule,
                                IsFromDistrict = false,
                                SourceEntityId = homeBuilding
                            });
                        }
                    }
                }

                // Collect from workplace building
                if (buildingConfigs.TryGetValue(workplaceBuilding, out var workplaceConfig))
                {
                    for (int i = 0; i < workplaceConfig.Rules.Length; i++)
                    {
                        var rule = workplaceConfig.Rules[i];
                        if (rule.TransportType == (byte)TransportType.Workers)
                        {
                            relevantRules.Add(new RuleData
                            {
                                Rule = rule,
                                IsFromDistrict = false,
                                SourceEntityId = workplaceBuilding
                            });
                        }
                    }
                }

                // Collect from districts
                if (homeDistrict != Entity.Null && districtConfigs.TryGetValue(homeDistrict.Index, out var homeDistrictConfig))
                {
                    for (int i = 0; i < homeDistrictConfig.Rules.Length; i++)
                    {
                        var rule = homeDistrictConfig.Rules[i];
                        if (rule.TransportType == (byte)TransportType.Workers)
                        {
                            relevantRules.Add(new RuleData
                            {
                                Rule = rule,
                                IsFromDistrict = true,
                                SourceEntityId = homeDistrict.Index
                            });
                        }
                    }
                }

                if (workplaceDistrict != Entity.Null && districtConfigs.TryGetValue(workplaceDistrict.Index, out var workplaceDistrictConfig))
                {
                    for (int i = 0; i < workplaceDistrictConfig.Rules.Length; i++)
                    {
                        var rule = workplaceDistrictConfig.Rules[i];
                        if (rule.TransportType == (byte)TransportType.Workers)
                        {
                            relevantRules.Add(new RuleData
                            {
                                Rule = rule,
                                IsFromDistrict = true,
                                SourceEntityId = workplaceDistrict.Index
                            });
                        }
                    }
                }

                if (relevantRules.Length == 0)
                {
                    relevantRules.Dispose();
                    return true;
                }

                // Filter by direction
                var directionFiltered = new NativeList<RuleData>(relevantRules.Length, Allocator.Temp);
                for (int i = 0; i < relevantRules.Length; i++)
                {
                    var ruleData = relevantRules[i];
                    bool isFromHome = ruleData.SourceEntityId == homeBuilding ||
                                      (homeDistrict != Entity.Null && ruleData.SourceEntityId == homeDistrict.Index && ruleData.IsFromDistrict);
                    bool isFromWorkplace = ruleData.SourceEntityId == workplaceBuilding ||
                                           (workplaceDistrict != Entity.Null && ruleData.SourceEntityId == workplaceDistrict.Index && ruleData.IsFromDistrict);

                    if ((isFromHome && ruleData.Rule.Type == (byte)ChainType.Outgoing) ||
                        (isFromWorkplace && ruleData.Rule.Type == (byte)ChainType.Incoming))
                    {
                        directionFiltered.Add(ruleData);
                    }
                }

                if (directionFiltered.Length == 0)
                {
                    relevantRules.Dispose();
                    directionFiltered.Dispose();
                    return true;
                }

                // Remove district rules if building rules exist
                bool hasBuildingRules = false;
                for (int i = 0; i < directionFiltered.Length; i++)
                {
                    if (!directionFiltered[i].IsFromDistrict)
                    {
                        hasBuildingRules = true;
                        break;
                    }
                }

                var finalRules = new NativeList<RuleData>(directionFiltered.Length, Allocator.Temp);
                for (int i = 0; i < directionFiltered.Length; i++)
                {
                    if (!hasBuildingRules || !directionFiltered[i].IsFromDistrict)
                    {
                        finalRules.Add(directionFiltered[i]);
                    }
                }

                bool result = EvaluateRulesJob(finalRules, homeBuilding, workplaceBuilding, homeDistrict, workplaceDistrict);

                relevantRules.Dispose();
                directionFiltered.Dispose();
                finalRules.Dispose();

                return result;
            }

            private static bool EvaluateRulesJob(NativeList<RuleData> rules, int homeBuilding, int workplaceBuilding,
                Entity homeDistrict, Entity workplaceDistrict)
            {
                bool hasAllowRules = false;
                bool hasMatchingAllowRule = false;
                bool hasMatchingDisallowRule = false;

                for (int i = 0; i < rules.Length; i++)
                {
                    var ruleData = rules[i];
                    var rule = ruleData.Rule;

                    int targetBuilding;
                    Entity targetDistrict;

                    if (rule.Type == (byte)ChainType.Outgoing)
                    {
                        targetBuilding = workplaceBuilding;
                        targetDistrict = workplaceDistrict;
                    }
                    else
                    {
                        targetBuilding = homeBuilding;
                        targetDistrict = homeDistrict;
                    }

                    bool isInList = ContainsBuilding(rule.Buildings, targetBuilding) ||
                                    (targetDistrict != Entity.Null && ContainsDistrict(rule.Districts, targetDistrict.Index));

                    if (rule.Allow == (byte)AllowType.Allow)
                    {
                        hasAllowRules = true;
                        if (isInList)
                        {
                            hasMatchingAllowRule = true;
                        }
                    }
                    else
                    {
                        if (isInList)
                        {
                            hasMatchingDisallowRule = true;
                        }
                    }
                }

                if (hasMatchingDisallowRule)
                    return false;
                if (hasAllowRules && !hasMatchingAllowRule)
                    return false;
                return true;
            }

            private static bool ContainsBuilding(NativeArray<int> buildings, int buildingId)
            {
                for (int i = 0; i < buildings.Length; i++)
                {
                    if (buildings[i] == buildingId)
                        return true;
                }
                return false;
            }

            private static bool ContainsDistrict(NativeArray<int> districts, int districtId)
            {
                for (int i = 0; i < districts.Length; i++)
                {
                    if (districts[i] == districtId)
                        return true;
                }
                return false;
            }

            private static Entity GetBuildingDistrictJob(Entity building, ComponentLookup<CurrentDistrict> currentDistrictLookup)
            {
                if (currentDistrictLookup.HasComponent(building))
                {
                    var currentDistrict = currentDistrictLookup[building];
                    if (currentDistrict.m_District != Entity.Null)
                    {
                        return currentDistrict.m_District;
                    }
                }
                return Entity.Null;
            }
        }

        /// <summary>
        /// Data structure for worker removal (can't remove directly in parallel job)
        /// </summary>
        private struct WorkerRemovalData
        {
            public Entity CitizenEntity;
            public Entity WorkplaceEntity;
        }

        /// <summary>
        /// Blittable rule data for job system
        /// </summary>
        private struct RuleData
        {
            public RuleBlittable Rule;
            public bool IsFromDistrict;
            public int SourceEntityId;
        }

        /// <summary>
        /// Blittable version of ResourceChainRule for use in Burst-compiled jobs
        /// </summary>
        private struct RuleBlittable
        {
            public byte Type; // ChainType
            public byte Allow; // AllowType
            public byte TransportType; // TransportType
            public NativeArray<int> Buildings;
            public NativeArray<int> Districts;
        }

        /// <summary>
        /// Blittable config data for job system
        /// </summary>
        private struct ConfigData
        {
            public NativeArray<RuleBlittable> Rules;
        }

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

            Mod.log.Info($"{nameof(ResourceChainRulesSystem)} created - Worker restriction enforcement enabled");
        }

        protected override void OnUpdate()
        {
            // Schedule parallel job to check worker restrictions
            var workersToRemove = new NativeQueue<WorkerRemovalData>(Allocator.TempJob);
            
            // Convert managed configs to native data structures for the job
            var buildingConfigs = ConvertToNativeConfigs(m_ResourceChainManagementSystem.GetAllConfigurations());
            var districtConfigs = ConvertToNativeConfigs(m_ResourceChainManagementSystem.GetAllDistrictConfigurations());

            // Only proceed if there are rules to enforce
            if (buildingConfigs.Count > 0 || districtConfigs.Count > 0)
            {
                var job = new CheckWorkerRestrictionsJob
                {
                    EntityType = GetEntityTypeHandle(),
                    WorkerType = GetComponentTypeHandle<Worker>(true),
                    HouseholdMemberType = GetComponentTypeHandle<HouseholdMember>(true),
                    PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true),
                    CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true),
                    BuildingConfigs = buildingConfigs,
                    DistrictConfigs = districtConfigs,
                    WorkersToRemove = workersToRemove.AsParallelWriter()
                };

                // Schedule the job to run in parallel
                Dependency = job.ScheduleParallel(m_WorkerQuery, Dependency);
                Dependency.Complete(); // Must complete before we process removal queue

                // Process removal queue on main thread (can't do structural changes in parallel)
                ProcessWorkerRemovals(workersToRemove);
            }

            // Cleanup
            DisposeNativeConfigs(buildingConfigs);
            DisposeNativeConfigs(districtConfigs);
            workersToRemove.Dispose();
        }

        /// <summary>
        /// Process the queue of workers that need to be removed (must be done on main thread)
        /// </summary>
        private void ProcessWorkerRemovals(NativeQueue<WorkerRemovalData> workersToRemove)
        {
            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var employeeBufferLookup = GetBufferLookup<Employee>();

            while (workersToRemove.TryDequeue(out var removal))
            {
                // Remove worker from the workplace's employee list
                if (employeeBufferLookup.HasBuffer(removal.WorkplaceEntity))
                {
                    var employees = employeeBufferLookup[removal.WorkplaceEntity];
                    for (int j = 0; j < employees.Length; j++)
                    {
                        if (employees[j].m_Worker == removal.CitizenEntity)
                        {
                            employees.RemoveAt(j);
                            break;
                        }
                    }
                }

                // Remove Worker component from citizen
                ecb.RemoveComponent<Worker>(removal.CitizenEntity);
            }
        }

        /// <summary>
        /// Convert managed configuration dictionary to native hash map for use in jobs
        /// </summary>
        private NativeHashMap<int, ConfigData> ConvertToNativeConfigs(Dictionary<int, BuildingConfiguration> configs)
        {
            if (configs == null || configs.Count == 0)
                return new NativeHashMap<int, ConfigData>(0, Allocator.TempJob);

            var nativeConfigs = new NativeHashMap<int, ConfigData>(configs.Count, Allocator.TempJob);

            foreach (var kvp in configs)
            {
                var rules = new NativeArray<RuleBlittable>(kvp.Value.Rules.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                
                for (int i = 0; i < kvp.Value.Rules.Count; i++)
                {
                    var managedRule = kvp.Value.Rules[i];
                    var buildings = new NativeArray<int>(managedRule.Buildings.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    var districts = new NativeArray<int>(managedRule.Districts.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                    for (int j = 0; j < managedRule.Buildings.Count; j++)
                        buildings[j] = managedRule.Buildings[j];
                    for (int j = 0; j < managedRule.Districts.Count; j++)
                        districts[j] = managedRule.Districts[j];

                    rules[i] = new RuleBlittable
                    {
                        Type = (byte)managedRule.Type,
                        Allow = (byte)managedRule.Allow,
                        TransportType = (byte)managedRule.TransportType,
                        Buildings = buildings,
                        Districts = districts
                    };
                }

                nativeConfigs[kvp.Key] = new ConfigData { Rules = rules };
            }

            return nativeConfigs;
        }

        /// <summary>
        /// Dispose native config data structures
        /// </summary>
        private void DisposeNativeConfigs(NativeHashMap<int, ConfigData> configs)
        {
            if (!configs.IsCreated)
                return;

            foreach (var kvp in configs)
            {
                var configData = kvp.Value;
                for (int i = 0; i < configData.Rules.Length; i++)
                {
                    if (configData.Rules[i].Buildings.IsCreated)
                        configData.Rules[i].Buildings.Dispose();
                    if (configData.Rules[i].Districts.IsCreated)
                        configData.Rules[i].Districts.Dispose();
                }
                if (configData.Rules.IsCreated)
                    configData.Rules.Dispose();
            }
            configs.Dispose();
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