﻿using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Pathfind;
using Game.Simulation;
using ManageResourceChains.Data;
using System.Collections.Generic;
using System.Linq;
using Game.Areas;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

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
        /// </summary>
        private Entity GetBuildingDistrict(Entity building)
        {
            Mod.log.Info($"🔍 GetBuildingDistrict for building {building.Index}");
            
            if (EntityManager.HasComponent<CurrentDistrict>(building))
            {
                var currentDistrict = EntityManager.GetComponentData<CurrentDistrict>(building);
                Mod.log.Info($"  ✓ Building {building.Index} belongs to district {currentDistrict.m_District.Index}");
                return currentDistrict.m_District;
            }
            
            Mod.log.Info($"  ✗ Building {building.Index} has no CurrentDistrict component");
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

            // Iterate through all workers
            var workers = m_WorkerQuery.ToEntityArray(Allocator.Temp);
            var workerComponents = m_WorkerQuery.ToComponentDataArray<Worker>(Allocator.Temp);
            var householdMembers = m_WorkerQuery.ToComponentDataArray<HouseholdMember>(Allocator.Temp);

            int removedCount = 0;
            
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
                
                bool isAllowed = IsWorkerTransportAllowed(homeId, workplaceId, TransportType.Workers);
                
                if (!isAllowed)
                {
                    Mod.log.Info($"🚫 [WORKER RESTRICTION] Removing worker {citizenEntity.Index} from workplace {workplaceId}. Home: {homeId}");
                    
                    // Remove worker from the workplace's employee list
                    if (employeeBufferLookup.HasBuffer(workplace))
                    {
                        var employees = employeeBufferLookup[workplace];
                        for (int j = 0; j < employees.Length; j++)
                        {
                            if (employees[j].m_Worker == citizenEntity)
                            {
                                employees.RemoveAt(j);
                                Mod.log.Info($"  ✓ Removed from employee list at index {j}");
                                break;
                            }
                        }
                    }
                    
                    // Remove Worker component from citizen
                    ecb.RemoveComponent<Worker>(citizenEntity);
                    removedCount++;
                    
                    Mod.log.Info($"  ✓ Worker component removed, citizen is now unemployed");
                }
            }

            if (removedCount > 0)
            {
                Mod.log.Info($"✅ [WORKER ENFORCEMENT] Removed {removedCount} workers from disallowed workplaces");
            }

            workers.Dispose();
            workerComponents.Dispose();
            householdMembers.Dispose();
        }

        /// <summary>
        /// Check if worker transport is allowed between a home and a workplace based on active rules.
        /// Checks BOTH building-level and district-level rules.
        /// NEW LOGIC:
        /// - DISALLOW = Blacklist (allow everything EXCEPT listed buildings)
        /// - ALLOW = Whitelist (allow ONLY listed buildings, block everything else)
        /// </summary>
        /// <param name="homeBuilding">Home building entity ID</param>
        /// <param name="workplaceBuilding">Workplace building entity ID</param>
        /// <param name="transportType">Type of transport (should be Workers)</param>
        /// <returns>True if transport is allowed</returns>
        public bool IsWorkerTransportAllowed(int homeBuilding, int workplaceBuilding, TransportType transportType)
        {
            if (transportType != TransportType.Workers)
                return true; // Only enforce worker restrictions for now

            Mod.log.Info($"🔎 Checking worker transport: Home {homeBuilding} -> Workplace {workplaceBuilding}");

            // Get all building configurations
            var buildingConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            
            // Get all district configurations
            var districtConfigs = m_ResourceChainManagementSystem.GetAllDistrictConfigurations();
            
            Mod.log.Info($"  Building configs: {buildingConfigs?.Count ?? 0}, District configs: {districtConfigs?.Count ?? 0}");
            
            if ((buildingConfigs == null || buildingConfigs.Count == 0) && 
                (districtConfigs == null || districtConfigs.Count == 0))
            {
                Mod.log.Info($"  No rules configured, allowing");
                return true; // No rules, allow everything
            }

            // Convert building IDs to entities to check districts
            Entity homeEntity = new Entity { Index = homeBuilding, Version = 0 };
            Entity workplaceEntity = new Entity { Index = workplaceBuilding, Version = 0 };
            
            // Get districts for both buildings
            Entity homeDistrict = GetBuildingDistrict(homeEntity);
            Entity workplaceDistrict = GetBuildingDistrict(workplaceEntity);

            // Check BUILDING-LEVEL rules first (they take priority)
            if (buildingConfigs != null)
            {
                foreach (var config in buildingConfigs.Values)
                {
                    foreach (var rule in config.Rules)
                    {
                        // Skip if not a worker rule
                        if (rule.TransportType != TransportType.Workers)
                            continue;

                        // Check OUTGOING rules from HOME
                        if (rule.Type == ChainType.Outgoing && config.BuildingEntityId == homeBuilding)
                        {
                            bool isInList = rule.Buildings.Contains(workplaceBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (Building Blacklist): Home {homeBuilding} -> Workplace {workplaceBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (Building Whitelist): Home {homeBuilding} -> Workplace {workplaceBuilding} (OUTGOING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }

                        // Check INCOMING rules to WORKPLACE
                        if (rule.Type == ChainType.Incoming && config.BuildingEntityId == workplaceBuilding)
                        {
                            bool isInList = rule.Buildings.Contains(homeBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (Building Blacklist): Home {homeBuilding} -> Workplace {workplaceBuilding} (INCOMING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (Building Whitelist): Home {homeBuilding} -> Workplace {workplaceBuilding} (INCOMING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Check DISTRICT-LEVEL rules (apply if building has no specific rules)
            if (districtConfigs != null)
            {
                Mod.log.Info($"  Checking district rules...");
                
                // Check home district rules (OUTGOING)
                if (homeDistrict != Entity.Null && districtConfigs.TryGetValue(homeDistrict.Index, out var homeDistrictConfig))
                {
                    Mod.log.Info($"  Home district {homeDistrict.Index} has {homeDistrictConfig.Rules.Count} rules");
                    
                    foreach (var rule in homeDistrictConfig.Rules)
                    {
                        // Skip if not a worker rule
                        if (rule.TransportType != TransportType.Workers)
                        {
                            Mod.log.Info($"    Rule {rule.Id}: Skipping (not a worker rule, type={rule.TransportType})");
                            continue;
                        }

                        Mod.log.Info($"    Rule {rule.Id}: Type={rule.Type}, Allow={rule.Allow}, Buildings={rule.Buildings.Count}");

                        // Check OUTGOING rules from HOME DISTRICT
                        if (rule.Type == ChainType.Outgoing)
                        {
                            bool isInList = rule.Buildings.Contains(workplaceBuilding);
                            Mod.log.Info($"    Workplace {workplaceBuilding} in list: {isInList}");
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (District Blacklist): Home {homeBuilding} (District {homeDistrict.Index}) -> Workplace {workplaceBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (District Whitelist): Home {homeBuilding} (District {homeDistrict.Index}) -> Workplace {workplaceBuilding} (OUTGOING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            Mod.log.Info($"    Rule {rule.Id}: Not an outgoing rule");
                        }
                    }
                }
                else if (homeDistrict != Entity.Null)
                {
                    Mod.log.Info($"  Home district {homeDistrict.Index} has no configuration");
                }
                else
                {
                    Mod.log.Info($"  Home building not in any district");
                }

                // Check workplace district rules (INCOMING)
                if (workplaceDistrict != Entity.Null && districtConfigs.TryGetValue(workplaceDistrict.Index, out var workplaceDistrictConfig))
                {
                    foreach (var rule in workplaceDistrictConfig.Rules)
                    {
                        // Skip if not a worker rule
                        if (rule.TransportType != TransportType.Workers)
                            continue;

                        // Check INCOMING rules to WORKPLACE DISTRICT
                        if (rule.Type == ChainType.Incoming)
                        {
                            bool isInList = rule.Buildings.Contains(homeBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (District Blacklist): Home {homeBuilding} -> Workplace {workplaceBuilding} (District {workplaceDistrict.Index}) (INCOMING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Worker BLOCKED (District Whitelist): Home {homeBuilding} -> Workplace {workplaceBuilding} (District {workplaceDistrict.Index}) (INCOMING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // No blocking rules found, allow transport
            return true;
        }

        /// <summary>
        /// Check if transport is allowed between two buildings based on active rules.
        /// Checks BOTH building-level and district-level rules.
        /// NEW LOGIC:
        /// - DISALLOW = Blacklist (allow everything EXCEPT listed buildings)
        /// - ALLOW = Whitelist (allow ONLY listed buildings, block everything else)
        /// </summary>
        /// <param name="sourceBuilding">Source building entity</param>
        /// <param name="targetBuilding">Target building entity</param>
        /// <param name="transportType">Type of transport (Workers/Services/Resources)</param>
        /// <returns>True if transport is allowed</returns>
        public bool IsTransportAllowed(int sourceBuilding, int targetBuilding, TransportType transportType)
        {
            // Get all building configurations
            var buildingConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            
            // Get all district configurations
            var districtConfigs = m_ResourceChainManagementSystem.GetAllDistrictConfigurations();
            
            if ((buildingConfigs == null || buildingConfigs.Count == 0) && 
                (districtConfigs == null || districtConfigs.Count == 0))
                return true; // No rules, allow everything

            // Convert building IDs to entities to check districts
            Entity sourceEntity = new Entity { Index = sourceBuilding, Version = 0 };
            Entity targetEntity = new Entity { Index = targetBuilding, Version = 0 };
            
            // Get districts for both buildings
            Entity sourceDistrict = GetBuildingDistrict(sourceEntity);
            Entity targetDistrict = GetBuildingDistrict(targetEntity);
            
            // Check BUILDING-LEVEL rules first (they take priority)
            if (buildingConfigs != null)
            {
                foreach (var config in buildingConfigs.Values)
                {
                    foreach (var rule in config.Rules)
                    {
                        // Skip if not matching transport type
                        if (rule.TransportType != transportType)
                            continue;
                        
                        // Check OUTGOING rules from SOURCE
                        if (rule.Type == ChainType.Outgoing && config.BuildingEntityId == sourceBuilding)
                        {
                            bool isInList = rule.Buildings.Contains(targetBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (Building Blacklist): Source {sourceBuilding} -> Target {targetBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (Building Whitelist): Source {sourceBuilding} -> Target {targetBuilding} (OUTGOING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                        
                        // Check INCOMING rules to TARGET
                        if (rule.Type == ChainType.Incoming && config.BuildingEntityId == targetBuilding)
                        {
                            bool isInList = rule.Buildings.Contains(sourceBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (Building Blacklist): Source {sourceBuilding} -> Target {targetBuilding} (INCOMING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (Building Whitelist): Source {sourceBuilding} -> Target {targetBuilding} (INCOMING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                    }
                }
            }

            // Check DISTRICT-LEVEL rules
            if (districtConfigs != null)
            {
                // Check source district rules (OUTGOING)
                if (sourceDistrict != Entity.Null && districtConfigs.TryGetValue(sourceDistrict.Index, out var sourceDistrictConfig))
                {
                    foreach (var rule in sourceDistrictConfig.Rules)
                    {
                        // Skip if not matching transport type
                        if (rule.TransportType != transportType)
                            continue;

                        // Check OUTGOING rules from SOURCE DISTRICT
                        if (rule.Type == ChainType.Outgoing)
                        {
                            bool isInList = rule.Buildings.Contains(targetBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (District Blacklist): Source {sourceBuilding} (District {sourceDistrict.Index}) -> Target {targetBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (District Whitelist): Source {sourceBuilding} (District {sourceDistrict.Index}) -> Target {targetBuilding} (OUTGOING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                    }
                }

                // Check target district rules (INCOMING)
                if (targetDistrict != Entity.Null && districtConfigs.TryGetValue(targetDistrict.Index, out var targetDistrictConfig))
                {
                    foreach (var rule in targetDistrictConfig.Rules)
                    {
                        // Skip if not matching transport type
                        if (rule.TransportType != transportType)
                            continue;

                        // Check INCOMING rules to TARGET DISTRICT
                        if (rule.Type == ChainType.Incoming)
                        {
                            bool isInList = rule.Buildings.Contains(sourceBuilding);
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (District Blacklist): Source {sourceBuilding} -> Target {targetBuilding} (District {targetDistrict.Index}) (INCOMING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    Mod.log.Info($"🚫 Transport BLOCKED (District Whitelist): Source {sourceBuilding} -> Target {targetBuilding} (District {targetDistrict.Index}) (INCOMING ALLOW rule '{rule.Id}' - not in allowed list)");
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
            
            // No blocking rules found, allow transport
            return true;
        }

        /// <summary>
        /// Get all rules that apply to a specific building and transport type
        /// </summary>
        private List<ResourceChainRule> GetApplicableRules(int buildingEntity, TransportType transportType, bool isSource)
        {
            var applicableRules = new List<ResourceChainRule>();
            var allConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            
            foreach (var config in allConfigs.Values)
            {
                foreach (var rule in config.Rules)
                {
                    // Check if this rule applies to this transport type
                    if (rule.TransportType != transportType)
                        continue;
                    
                    // Check if this building is affected by the rule
                    if (rule.Buildings.Contains(buildingEntity))
                    {
                        // Check if the direction matches (source vs destination)
                        bool matchesDirection = (rule.Type == ChainType.Outgoing && isSource) ||
                                              (rule.Type == ChainType.Incoming && !isSource);
                        
                        if (matchesDirection)
                        {
                            applicableRules.Add(rule);
                        }
                    }
                }
            }
            
            return applicableRules;
        }

        /// <summary>
        /// Calculate a penalty cost for pathfinding based on rules
        /// This can be used to make disallowed paths extremely expensive rather than blocking them entirely
        /// </summary>
        public float CalculatePathPenalty(int sourceBuilding, int targetBuilding, TransportType transportType)
        {
            if (!IsTransportAllowed(sourceBuilding, targetBuilding, transportType))
            {
                // Return a very high penalty to effectively block the path
                return 1000000f;
            }
            
            return 0f;
        }
    }
}

