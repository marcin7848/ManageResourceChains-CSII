using Game;
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

            // Get all configurations
            var allConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            if (allConfigs == null || allConfigs.Count == 0)
                return true; // No rules, allow everything

            // Check rules from the HOME building (citizens going OUT to work)
            foreach (var config in allConfigs.Values)
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
                                Mod.log.Info($"🚫 Worker BLOCKED (Blacklist): Home {homeBuilding} -> Workplace {workplaceBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                return false;
                            }
                        }
                        else // AllowType.Allow
                        {
                            // ALLOW = Whitelist: Block if NOT in the list
                            if (!isInList)
                            {
                                Mod.log.Info($"🚫 Worker BLOCKED (Whitelist): Home {homeBuilding} -> Workplace {workplaceBuilding} (OUTGOING ALLOW rule '{rule.Id}' - not in allowed list)");
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
                                Mod.log.Info($"🚫 Worker BLOCKED (Blacklist): Home {homeBuilding} -> Workplace {workplaceBuilding} (INCOMING DISALLOW rule '{rule.Id}')");
                                return false;
                            }
                        }
                        else // AllowType.Allow
                        {
                            // ALLOW = Whitelist: Block if NOT in the list
                            if (!isInList)
                            {
                                Mod.log.Info($"🚫 Worker BLOCKED (Whitelist): Home {homeBuilding} -> Workplace {workplaceBuilding} (INCOMING ALLOW rule '{rule.Id}' - not in allowed list)");
                                return false;
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
            var allConfigs = m_ResourceChainManagementSystem.GetAllConfigurations();
            if (allConfigs == null || allConfigs.Count == 0)
                return true; // No rules, allow everything
            
            // Check all configurations for applicable rules
            foreach (var config in allConfigs.Values)
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
                                Mod.log.Info($"🚫 Transport BLOCKED (Blacklist): Source {sourceBuilding} -> Target {targetBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                return false;
                            }
                        }
                        else // AllowType.Allow
                        {
                            // ALLOW = Whitelist: Block if NOT in the list
                            if (!isInList)
                            {
                                Mod.log.Info($"🚫 Transport BLOCKED (Whitelist): Source {sourceBuilding} -> Target {targetBuilding} (OUTGOING ALLOW rule '{rule.Id}' - not in allowed list)");
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
                                Mod.log.Info($"🚫 Transport BLOCKED (Blacklist): Source {sourceBuilding} -> Target {targetBuilding} (INCOMING DISALLOW rule '{rule.Id}')");
                                return false;
                            }
                        }
                        else // AllowType.Allow
                        {
                            // ALLOW = Whitelist: Block if NOT in the list
                            if (!isInList)
                            {
                                Mod.log.Info($"🚫 Transport BLOCKED (Whitelist): Source {sourceBuilding} -> Target {targetBuilding} (INCOMING ALLOW rule '{rule.Id}' - not in allowed list)");
                                return false;
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

