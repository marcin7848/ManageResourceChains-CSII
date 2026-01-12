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
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts pathfinding requests and applies resource chain rules
    /// to block or allow transport of workers, services, and resources between buildings.
    /// </summary>
    public partial class ResourceChainPathfindSystem : GameSystemBase
    {
        private ResourceChainManagementSystem m_ResourceChainManagementSystem;
        private EntityQuery m_PathfindQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            
            // Query for entities with pathfind setups
            m_PathfindQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] 
                { 
                    ComponentType.ReadOnly<PathOwner>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>()
                }
            });
            
            Mod.log.Info($"{nameof(ResourceChainPathfindSystem)} created");
        }

        protected override void OnUpdate()
        {
            // This system will be called during pathfinding setup
            // We'll check pathfind requests and filter out disallowed targets
            
            // Note: This is a placeholder structure. The actual implementation
            // will need to hook into the PathfindSetupSystem's execution flow.
            // 
            // Possible approaches:
            // 1. Modify PathfindParameters.m_MaxCost for disallowed paths
            // 2. Add custom validation logic to PathfindTargetSeeker
            // 3. Create a pre-pathfind filter that marks buildings as unavailable
        }

        /// <summary>
        /// Check if transport is allowed between two buildings based on active rules
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

