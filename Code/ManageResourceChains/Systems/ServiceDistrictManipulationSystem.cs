using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using ManageResourceChains.Data;
using System.Collections.Generic;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that manipulates ServiceDistrict buffers on service buildings based on rules.
    /// This works WITH Burst-compiled pathfinding code instead of trying to patch it.
    /// 
    /// The game's pathfinding checks ServiceDistrict buffers to determine which buildings
    /// a service can serve. By modifying these buffers according to our rules, we make
    /// the Burst-compiled pathfinding code naturally respect our restrictions.
    /// </summary>
    public partial class ServiceDistrictManipulationSystem : GameSystemBase
    {
        private ResourceChainManagementSystem m_ManagementSystem;
        private ResourceChainRulesSystem m_RulesSystem;
        private EntityQuery m_ServiceBuildingQuery;
        private EntityQuery m_DistrictQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            m_ManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            m_RulesSystem = World.GetOrCreateSystemManaged<ResourceChainRulesSystem>();

            // Query for all service buildings that might have ServiceDistrict buffers
            m_ServiceBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<ServiceDistrict>(),
                    ComponentType.ReadOnly<Building>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            // Query for all districts
            m_DistrictQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<District>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            Mod.log.Info($"{nameof(ServiceDistrictManipulationSystem)} created - will manipulate ServiceDistrict buffers");
        }

        protected override void OnUpdate()
        {
            // Get all building configurations with rules
            var buildingConfigs = m_ManagementSystem.GetAllConfigurations();
            if (buildingConfigs == null || buildingConfigs.Count == 0)
                return;

            var serviceDistrictLookup = GetBufferLookup<ServiceDistrict>(false);
            var currentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            var districtLookup = GetComponentLookup<District>(true);

            // Get all districts in the city
            var allDistricts = m_DistrictQuery.ToEntityArray(Allocator.Temp);

            // For each service building with rules
            foreach (var kvp in buildingConfigs)
            {
                int buildingId = kvp.Key;
                var config = kvp.Value;

                // Check if this building has any OUTGOING service rules
                bool hasServiceRules = false;
                foreach (var rule in config.Rules)
                {
                    if (rule.TransportType == TransportType.Services && rule.Type == ChainType.Outgoing)
                    {
                        hasServiceRules = true;
                        break;
                    }
                }

                if (!hasServiceRules)
                    continue;

                // Get the entity
                Entity buildingEntity = new Entity { Index = buildingId, Version = 1 };

                // Check if it has a ServiceDistrict buffer
                if (!serviceDistrictLookup.TryGetBuffer(buildingEntity, out var serviceDistricts))
                    continue;

                Mod.log.Info($"Manipulating ServiceDistrict buffer for building {buildingId}...");

                // Clear the buffer
                serviceDistricts.Clear();

                // Process each rule
                bool hasAllowRules = false;
                var allowedDistricts = new HashSet<int>();
                var disallowedBuildings = new HashSet<int>();
                var disallowedDistricts = new HashSet<int>();

                foreach (var rule in config.Rules)
                {
                    if (rule.TransportType != TransportType.Services || rule.Type != ChainType.Outgoing)
                        continue;

                    if (rule.Allow == AllowType.Allow)
                    {
                        hasAllowRules = true;
                        // ALLOW rule: Add these districts
                        foreach (var districtId in rule.Districts)
                        {
                            allowedDistricts.Add(districtId);
                        }
                    }
                    else // DISALLOW
                    {
                        // DISALLOW rule: Track which districts to exclude
                        foreach (var districtId in rule.Districts)
                        {
                            disallowedDistricts.Add(districtId);
                        }
                        
                        // For disallowed buildings, find their districts
                        foreach (var targetBuildingId in rule.Buildings)
                        {
                            disallowedBuildings.Add(targetBuildingId);
                            
                            Entity targetEntity = new Entity { Index = targetBuildingId, Version = 1 };
                            if (currentDistrictLookup.TryGetComponent(targetEntity, out var currentDistrict))
                            {
                                if (currentDistrict.m_District != Entity.Null)
                                {
                                    disallowedDistricts.Add(currentDistrict.m_District.Index);
                                    Mod.log.Info($"  -> Building {targetBuildingId} is in district {currentDistrict.m_District.Index} - will exclude");
                                }
                            }
                        }
                    }
                }

                // Apply the rules
                if (hasAllowRules)
                {
                    // ALLOW rules: Add only allowed districts
                    foreach (var districtId in allowedDistricts)
                    {
                        Entity districtEntity = new Entity { Index = districtId, Version = 1 };
                        serviceDistricts.Add(new ServiceDistrict(districtEntity));
                        Mod.log.Info($"  -> Added district {districtId} to ALLOW list");
                    }
                }
                else
                {
                    // DISALLOW rules only: Add all districts EXCEPT disallowed ones
                    foreach (var districtEntity in allDistricts)
                    {
                        if (!disallowedDistricts.Contains(districtEntity.Index))
                        {
                            serviceDistricts.Add(new ServiceDistrict(districtEntity));
                            Mod.log.Info($"  -> Added district {districtEntity.Index} (not in disallow list)");
                        }
                        else
                        {
                            Mod.log.Info($"  -> Excluded district {districtEntity.Index} (in disallow list)");
                        }
                    }
                }

                // Log result
                if (serviceDistricts.Length == 0)
                {
                    Mod.log.Info($"  -> Service building {buildingId} has NO allowed districts (fully restricted)");
                }
                else
                {
                    Mod.log.Info($"  -> Service building {buildingId} can serve {serviceDistricts.Length} district(s)");
                }
            }

            allDistricts.Dispose();
        }
    }
}
