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
                
                bool isAllowed = IsWorkerTransportAllowed(homeId, workplaceId, TransportType.Workers, homeBuilding, workplace, currentDistrictLookup);
                
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
        /// Check if worker transport is allowed between a home and a workplace based on active rules.
        /// Checks BOTH building-level and district-level rules.
        /// NEW LOGIC:
        /// - DISALLOW = Blacklist (allow everything EXCEPT listed buildings)
        /// - ALLOW = Whitelist (allow ONLY listed buildings, block everything else)
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
            
            // Get all district configurations
            var districtConfigs = m_ResourceChainManagementSystem.GetAllDistrictConfigurations();
            
            if ((buildingConfigs == null || buildingConfigs.Count == 0) && 
                (districtConfigs == null || districtConfigs.Count == 0))
            {
                return true; // No rules, allow everything
            }

            // Get districts for both buildings
            Entity homeDistrict = GetBuildingDistrict(homeEntity, currentDistrictLookup);
            Entity workplaceDistrict = GetBuildingDistrict(workplaceEntity, currentDistrictLookup);


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
                            // Check if workplace is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(workplaceBuilding) ||
                                          (workplaceDistrict != Entity.Null && rule.Districts.Contains(workplaceDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    return false;
                                }
                            }
                        }

                        // Check INCOMING rules to WORKPLACE
                        if (rule.Type == ChainType.Incoming && config.BuildingEntityId == workplaceBuilding)
                        {
                            // Check if home is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(homeBuilding) ||
                                          (homeDistrict != Entity.Null && rule.Districts.Contains(homeDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
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
                // Check home district rules (OUTGOING)
                if (homeDistrict != Entity.Null && districtConfigs.TryGetValue(homeDistrict.Index, out var homeDistrictConfig))
                {
                    foreach (var rule in homeDistrictConfig.Rules)
                    {
                        // Skip if not a worker rule
                        if (rule.TransportType != TransportType.Workers)
                            continue;

                        // Check OUTGOING rules from HOME DISTRICT
                        if (rule.Type == ChainType.Outgoing)
                        {
                            // Check if workplace is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(workplaceBuilding) ||
                                          (workplaceDistrict != Entity.Null && rule.Districts.Contains(workplaceDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    return false;
                                }
                            }
                        }
                    }
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
                            // Check if home is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(homeBuilding) ||
                                          (homeDistrict != Entity.Null && rule.Districts.Contains(homeDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
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
        /// <param name="sourceBuilding">Source building entity ID</param>
        /// <param name="targetBuilding">Target building entity ID</param>
        /// <param name="transportType">Type of transport (Workers/Services/Resources)</param>
        /// <param name="sourceEntity">Source building entity (for district lookup)</param>
        /// <param name="targetEntity">Target building entity (for district lookup)</param>
        /// <param name="currentDistrictLookup">ComponentLookup for CurrentDistrict</param>
        /// <returns>True if transport is allowed</returns>
        public bool IsTransportAllowed(int sourceBuilding, int targetBuilding, TransportType transportType,
            Entity sourceEntity, Entity targetEntity, ComponentLookup<CurrentDistrict> currentDistrictLookup)
        {
            // Get all building configurations
            var buildingConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            
            // Get all district configurations
            var districtConfigs = m_ResourceChainManagementSystem.GetAllDistrictConfigurations();
            
            if ((buildingConfigs == null || buildingConfigs.Count == 0) && 
                (districtConfigs == null || districtConfigs.Count == 0))
                return true; // No rules, allow everything

            // Get districts for both buildings
            Entity sourceDistrict = GetBuildingDistrict(sourceEntity, currentDistrictLookup);
            Entity targetDistrict = GetBuildingDistrict(targetEntity, currentDistrictLookup);
            
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
                            // Check if target is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(targetBuilding) ||
                                          (targetDistrict != Entity.Null && rule.Districts.Contains(targetDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
                                    return false;
                                }
                            }
                        }
                        
                        // Check INCOMING rules to TARGET
                        if (rule.Type == ChainType.Incoming && config.BuildingEntityId == targetBuilding)
                        {
                            // Check if source is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(sourceBuilding) ||
                                          (sourceDistrict != Entity.Null && rule.Districts.Contains(sourceDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
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
                            // Check if target is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(targetBuilding) ||
                                          (targetDistrict != Entity.Null && rule.Districts.Contains(targetDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
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
                            // Check if source is directly in the buildings list OR in one of the listed districts
                            bool isInList = rule.Buildings.Contains(sourceBuilding) ||
                                          (sourceDistrict != Entity.Null && rule.Districts.Contains(sourceDistrict.Index));
                            
                            if (rule.Allow == AllowType.Disallow)
                            {
                                // DISALLOW = Blacklist: Block if IN the list
                                if (isInList)
                                {
                                    return false;
                                }
                            }
                            else // AllowType.Allow
                            {
                                // ALLOW = Whitelist: Block if NOT in the list
                                if (!isInList)
                                {
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
        public float CalculatePathPenalty(int sourceBuilding, int targetBuilding, TransportType transportType,
            Entity sourceEntity, Entity targetEntity, ComponentLookup<CurrentDistrict> currentDistrictLookup)
        {
            if (!IsTransportAllowed(sourceBuilding, targetBuilding, transportType, sourceEntity, targetEntity, currentDistrictLookup))
            {
                // Return a very high penalty to effectively block the path
                return 1000000f;
            }
            
            return 0f;
        }
    }
}

