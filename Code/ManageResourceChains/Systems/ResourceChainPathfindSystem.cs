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
        /// Check if worker transport is allowed between a home and a workplace based on active rules
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
                    if (rule.Type == ChainType.Outgoing)
                    {
                        // Check if this home building has the rule
                        if (config.BuildingEntityId == homeBuilding)
                        {
                            // Check if the workplace is in the rule's building list
                            if (rule.Buildings.Contains(workplaceBuilding))
                            {
                                if (rule.Allow == AllowType.Disallow)
                                {
                                    Mod.log.Info($"🚫 Worker transport BLOCKED: Home {homeBuilding} -> Workplace {workplaceBuilding} (OUTGOING DISALLOW rule '{rule.Id}')");
                                    return false;
                                }
                            }
                        }
                    }

                    // Check INCOMING rules to WORKPLACE
                    if (rule.Type == ChainType.Incoming)
                    {
                        // Check if this workplace building has the rule
                        if (config.BuildingEntityId == workplaceBuilding)
                        {
                            // Check if the home is in the rule's building list
                            if (rule.Buildings.Contains(homeBuilding))
                            {
                                if (rule.Allow == AllowType.Disallow)
                                {
                                    Mod.log.Info($"🚫 Worker transport BLOCKED: Home {homeBuilding} -> Workplace {workplaceBuilding} (INCOMING DISALLOW rule '{rule.Id}')");
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
        /// Check if transport is allowed between two buildings based on active rules (legacy method)
        /// </summary>
        /// <param name="sourceBuilding">Source building entity</param>
        /// <param name="targetBuilding">Target building entity</param>
        /// <param name="transportType">Type of transport (Workers/Services/Resources)</param>
        /// <returns>True if transport is allowed</returns>
        public bool IsTransportAllowed(int sourceBuilding, int targetBuilding, TransportType transportType)
        {
            // Get rules for both source and target buildings
            var sourceRules = GetApplicableRules(sourceBuilding, transportType, true);
            var targetRules = GetApplicableRules(targetBuilding, transportType, false);
            
            // Check source rules (Outgoing)
            foreach (var rule in sourceRules)
            {
                if (rule.Type == ChainType.Outgoing)
                {
                    if (rule.Buildings.Contains(targetBuilding))
                    {
                        // This rule specifically mentions the target
                        if (rule.Allow == AllowType.Disallow)
                        {
                            Mod.log.Info($"Transport blocked by source rule {rule.Id}: {sourceBuilding} -> {targetBuilding}");
                            return false;
                        }
                    }
                }
            }
            
            // Check target rules (Incoming)
            foreach (var rule in targetRules)
            {
                if (rule.Type == ChainType.Incoming)
                {
                    if (rule.Buildings.Contains(sourceBuilding))
                    {
                        // This rule specifically mentions the source
                        if (rule.Allow == AllowType.Disallow)
                        {
                            Mod.log.Info($"Transport blocked by target rule {rule.Id}: {sourceBuilding} -> {targetBuilding}");
                            return false;
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

