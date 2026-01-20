using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Collections;
using Game;
using Game.Citizens;
using Game.Common;
using Game.Pathfind;
using Game.Simulation;
using ManageResourceChains.Data;
using Game.Buildings;
using Game.Creatures;
using Game.Routes;
using Game.Tools;
using Game.Prefabs;
using Game.Vehicles;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// Simplified Worker Transport Priority System V2
    /// 
    /// Key Design Change: Instead of trying to manually control which line the worker boards,
    /// this version uses a simpler multi-stage approach:
    /// 
    /// 1. When worker starts going to work, intercept and redirect target to the priority station
    /// 2. Let the GAME'S NATIVE AI handle the entire pathfinding to the station (including which lines to use!)
    /// 3. When worker reaches the station, switch target to final destination
    /// 4. Again, let the GAME handle pathfinding from station to work
    /// 
    /// This approach leverages the game's sophisticated pathfinding which already picks the best lines.
    /// The mod just forces the path to GO THROUGH specific stations.
    /// </summary>
    public partial class WorkerTransportPrioritySystemV2 : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_WorkerQuery;
        private EntityQuery m_ForcedTripQuery;
        
        private ComponentLookup<PropertyRenter> m_PropertyRenterLookup;
        private ComponentLookup<HouseholdMember> m_HouseholdMemberLookup;
        private ComponentLookup<Worker> m_WorkerLookup;
        private ComponentLookup<Target> m_TargetLookup;
        private ComponentLookup<PathOwner> m_PathOwnerLookup;
        private ComponentLookup<TravelPurpose> m_TravelPurposeLookup;
        private ComponentLookup<CurrentBuilding> m_CurrentBuildingLookup;
        private ComponentLookup<Building> m_BuildingLookup;
        private ComponentLookup<Game.Creatures.Resident> m_ResidentLookup;
        private ComponentLookup<HumanCurrentLane> m_HumanCurrentLaneLookup;
        private ComponentLookup<Connected> m_ConnectedLookup;
        private ComponentLookup<Waypoint> m_WaypointLookup;
        private ComponentLookup<Owner> m_OwnerLookup;
        private ComponentLookup<CurrentVehicle> m_CurrentVehicleLookup;
        private BufferLookup<PathElement> m_PathElementLookup;
        private BufferLookup<RouteWaypoint> m_RouteWaypointLookup;
        
        // Track active frame count to avoid updating too frequently
        private uint m_LastUpdateFrame;
        private const uint UPDATE_INTERVAL = 16; // Update every 16 frames
        
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            
            // Query for RESIDENTS (walking creatures) who might be going to work
            // The Resident component links to the Citizen entity which has Worker info
            // Target and PathOwner are on the creature, not the citizen
            m_WorkerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadOnly<Game.Creatures.Resident>(), // The walking human creature
                    ComponentType.ReadWrite<Target>(),
                    ComponentType.ReadWrite<PathOwner>()
                },
                None = new[] { 
                    ComponentType.ReadOnly<ForcedStationTrip>(), // Don't re-process already forced trips
                    ComponentType.ReadOnly<Deleted>(), 
                    ComponentType.ReadOnly<Temp>() 
                }
            });
            
            // Query for residents with active forced station trips
            m_ForcedTripQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadWrite<ForcedStationTrip>(),
                    ComponentType.ReadOnly<Game.Creatures.Resident>(),
                    ComponentType.ReadWrite<Target>(),
                    ComponentType.ReadWrite<PathOwner>()
                },
                None = new[] { 
                    ComponentType.ReadOnly<Deleted>(), 
                    ComponentType.ReadOnly<Temp>() 
                }
            });
            
            m_PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true);
            m_HouseholdMemberLookup = GetComponentLookup<HouseholdMember>(true);
            m_WorkerLookup = GetComponentLookup<Worker>(true);
            m_TargetLookup = GetComponentLookup<Target>(false);
            m_PathOwnerLookup = GetComponentLookup<PathOwner>(false);
            m_TravelPurposeLookup = GetComponentLookup<TravelPurpose>(true);
            m_CurrentBuildingLookup = GetComponentLookup<CurrentBuilding>(true);
            m_BuildingLookup = GetComponentLookup<Building>(true);
            m_ResidentLookup = GetComponentLookup<Game.Creatures.Resident>(false);
            m_HumanCurrentLaneLookup = GetComponentLookup<HumanCurrentLane>(false);
            m_ConnectedLookup = GetComponentLookup<Connected>(true);
            m_WaypointLookup = GetComponentLookup<Waypoint>(true);
            m_OwnerLookup = GetComponentLookup<Owner>(true);
            m_CurrentVehicleLookup = GetComponentLookup<CurrentVehicle>(true);
            m_PathElementLookup = GetBufferLookup<PathElement>(false);
            m_RouteWaypointLookup = GetBufferLookup<RouteWaypoint>(true);
            
            Mod.log.Info("WorkerTransportPrioritySystemV2 created - using simplified multi-stage approach");
        }

        protected override void OnUpdate()
        {
            // Only update periodically
            if (m_SimulationSystem.frameIndex - m_LastUpdateFrame < UPDATE_INTERVAL)
                return;
            m_LastUpdateFrame = m_SimulationSystem.frameIndex;
            
            // Update lookups
            m_PropertyRenterLookup.Update(this);
            m_HouseholdMemberLookup.Update(this);
            m_WorkerLookup.Update(this);
            m_TargetLookup.Update(this);
            m_PathOwnerLookup.Update(this);
            m_TravelPurposeLookup.Update(this);
            m_CurrentBuildingLookup.Update(this);
            m_BuildingLookup.Update(this);
            m_ResidentLookup.Update(this);
            m_HumanCurrentLaneLookup.Update(this);
            m_ConnectedLookup.Update(this);
            m_WaypointLookup.Update(this);
            m_OwnerLookup.Update(this);
            m_CurrentVehicleLookup.Update(this);
            m_PathElementLookup.Update(this);
            m_RouteWaypointLookup.Update(this);

            // Process new workers starting their commute
            InterceptNewCommutes();
            
            // Process workers on forced station trips
            ProcessForcedTrips();
        }

        /// <summary>
        /// Intercept workers starting their commute and set them up as waiting for transport
        /// at the designated bus stop/station. This bypasses the pathfinding to the stop
        /// and directly puts them in waiting state.
        /// </summary>
        private void InterceptNewCommutes()
        {
            if (m_WorkerQuery.IsEmptyIgnoreFilter) 
            {
                // Log occasionally to confirm system is running
                if (m_SimulationSystem.frameIndex % 256 == 0)
                {
                    Mod.log.Info($"[V2] No residents in query");
                }
                return;
            }

            var allConfigs = ResourceChainManagementSystem.GetActiveConfigurations();
            if (allConfigs.Count == 0) 
            {
                if (m_SimulationSystem.frameIndex % 256 == 0)
                {
                    Mod.log.Info($"[V2] No active configurations");
                }
                return;
            }

            var entities = m_WorkerQuery.ToEntityArray(Allocator.Temp);
            
            // Log occasionally to show how many entities we're processing
            if (m_SimulationSystem.frameIndex % 256 == 0)
            {
                Mod.log.Info($"[V2] Processing {entities.Length} residents, {allConfigs.Count} configs");
            }
            
            foreach (var residentEntity in entities)
            {
                // Get the Resident component to find the Citizen entity
                if (!m_ResidentLookup.TryGetComponent(residentEntity, out var resident))
                    continue;
                
                Entity citizenEntity = resident.m_Citizen;
                if (citizenEntity == Entity.Null)
                    continue;

                // Check if this is a GoingToWork trip (TravelPurpose is on the Citizen)
                if (!m_TravelPurposeLookup.TryGetComponent(citizenEntity, out var travelPurpose))
                    continue;
                    
                if (travelPurpose.m_Purpose != Purpose.GoingToWork)
                    continue;

                // Get worker info (Worker component is on the Citizen)
                if (!m_WorkerLookup.TryGetComponent(citizenEntity, out var worker))
                    continue;

                Entity workplaceEntity = worker.m_Workplace;
                if (workplaceEntity == Entity.Null)
                    continue;

                // Get home entity (from Citizen's household)
                Entity homeEntity = GetHomeEntity(citizenEntity);
                if (homeEntity == Entity.Null)
                    continue;

                // Log that we found a valid worker going to work
                Mod.log.Info($"[V2] Found worker: resident={residentEntity.Index}, citizen={citizenEntity.Index}, home={homeEntity.Index}, workplace={workplaceEntity.Index}");

                // Find applicable rule with transport priorities
                var rule = FindApplicableRuleWithPriorities(homeEntity, workplaceEntity);
                if (rule == null || rule.TransportPriorities.Count == 0)
                {
                    Mod.log.Info($"[V2] No matching rule for home={homeEntity.Index}, workplace={workplaceEntity.Index}");
                    continue;
                }

                Mod.log.Info($"[V2] Found matching rule! RuleId={rule.Id}");

                // Get the first priority station (bus stop entity)
                var firstPriority = rule.TransportPriorities[0];
                Entity stopEntity = new Entity { Index = firstPriority.StationEntity, Version = 1 };
                
                // Verify the stop entity exists
                if (!EntityManager.Exists(stopEntity))
                {
                    Mod.log.Warn($"Stop entity {firstPriority.StationEntity} doesn't exist, skipping forced trip");
                    continue;
                }

                // Get the HumanCurrentLane component
                if (!m_HumanCurrentLaneLookup.TryGetComponent(residentEntity, out var humanLane))
                {
                    Mod.log.Warn($"Resident {residentEntity.Index} has no HumanCurrentLane, skipping");
                    continue;
                }

                // Find the waypoint connected to this stop (needed for path setup)
                Entity waypointEntity = FindWaypointForStop(stopEntity);
                if (waypointEntity == Entity.Null)
                {
                    Mod.log.Warn($"Could not find waypoint for stop {stopEntity.Index}, skipping");
                    continue;
                }

                Mod.log.Info($"[V2] Setting up worker {citizenEntity.Index} as waiting at stop {stopEntity.Index} (waypoint {waypointEntity.Index})");

                // Create the forced station trip component
                var forcedTrip = new ForcedStationTrip
                {
                    m_FinalDestination = workplaceEntity,
                    m_CurrentStationTarget = stopEntity,
                    m_CurrentPriorityIndex = 0,
                    m_TotalPriorities = rule.TransportPriorities.Count,
                    m_State = ForcedStationTripState.WaitingAtStation,  // We're directly placing them at the station
                    m_StartFrame = m_SimulationSystem.frameIndex,
                    m_PathWasSet = true  // We're setting up the path manually
                };
                
                EntityManager.AddComponentData(residentEntity, forcedTrip);

                // Set target to the final workplace (the bus will take them there)
                var target = m_TargetLookup[residentEntity];
                target.m_Target = workplaceEntity;
                EntityManager.SetComponentData(residentEntity, target);

                // Set up the HumanCurrentLane to indicate waiting at transport stop
                humanLane.m_Lane = stopEntity;
                humanLane.m_Flags |= CreatureLaneFlags.Transport | CreatureLaneFlags.EndReached;
                EntityManager.SetComponentData(residentEntity, humanLane);

                // Set up the Resident flags for waiting transport
                resident.m_Flags |= ResidentFlags.WaitingTransport | ResidentFlags.NoLateDeparture;
                resident.m_Flags &= ~(ResidentFlags.Arrived | ResidentFlags.Hangaround);
                resident.m_Timer = 0;
                EntityManager.SetComponentData(residentEntity, resident);

                // Set up path with waypoint as the first element
                // The path structure tells the game where to board and where to get off
                if (m_PathElementLookup.HasBuffer(residentEntity))
                {
                    var pathElements = m_PathElementLookup[residentEntity];
                    pathElements.Clear();
                    
                    // Add the boarding waypoint
                    pathElements.Add(new PathElement(waypointEntity, 0f));
                    
                    // Find a waypoint near the workplace to exit at
                    // For now, just use the workplace as the destination
                    // The game will figure out where to exit
                }

                // Mark path as updated (not obsolete) since we set it up
                var pathOwner = m_PathOwnerLookup[residentEntity];
                pathOwner.m_State &= ~(PathFlags.Obsolete | PathFlags.Pending | PathFlags.Failed);
                pathOwner.m_State |= PathFlags.Updated;
                pathOwner.m_ElementIndex = 0;
                EntityManager.SetComponentData(residentEntity, pathOwner);

                Mod.log.Info($"[V2] Worker {citizenEntity.Index} is now waiting for transport at stop {stopEntity.Index}");
            }
            
            entities.Dispose();
        }

        /// <summary>
        /// Find a waypoint entity connected to a given stop
        /// </summary>
        private Entity FindWaypointForStop(Entity stopEntity)
        {
            // The stop might itself be a waypoint with Connected component
            if (m_ConnectedLookup.HasComponent(stopEntity))
            {
                var connected = m_ConnectedLookup[stopEntity];
                if (connected.m_Connected != Entity.Null)
                {
                    // stopEntity is the waypoint, connected is the actual stop
                    return stopEntity;
                }
            }

            // Or we need to find a waypoint that connects to this stop
            // The stop has an Owner (the route), and the route has RouteWaypoints
            if (m_OwnerLookup.TryGetComponent(stopEntity, out var owner))
            {
                Entity routeEntity = owner.m_Owner;
                if (m_RouteWaypointLookup.HasBuffer(routeEntity))
                {
                    var waypoints = m_RouteWaypointLookup[routeEntity];
                    foreach (var wp in waypoints)
                    {
                        if (m_ConnectedLookup.TryGetComponent(wp.m_Waypoint, out var wpConnected))
                        {
                            if (wpConnected.m_Connected == stopEntity)
                            {
                                return wp.m_Waypoint;
                            }
                        }
                    }
                }
            }

            // Fallback: return the stop entity itself
            return stopEntity;
        }

        /// <summary>
        /// Process workers who are on forced station trips
        /// </summary>
        private void ProcessForcedTrips()
        {
            if (m_ForcedTripQuery.IsEmptyIgnoreFilter) return;

            var entities = m_ForcedTripQuery.ToEntityArray(Allocator.Temp);
            var forcedTrips = m_ForcedTripQuery.ToComponentDataArray<ForcedStationTrip>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity residentEntity = entities[i];
                ForcedStationTrip forcedTrip = forcedTrips[i];

                // Get the current target
                if (!m_TargetLookup.TryGetComponent(residentEntity, out var target))
                    continue;
                
                // Get path owner to check path status
                if (!m_PathOwnerLookup.TryGetComponent(residentEntity, out var pathOwner))
                    continue;

                // Get current building (may be null for roadside stops)
                Entity currentBuildingEntity = Entity.Null;
                if (m_CurrentBuildingLookup.TryGetComponent(residentEntity, out var currentBuilding))
                {
                    currentBuildingEntity = currentBuilding.m_CurrentBuilding;
                }

                // Check if resident is in a vehicle
                bool inVehicle = m_CurrentVehicleLookup.HasComponent(residentEntity);
                Entity currentVehicle = inVehicle ? m_CurrentVehicleLookup[residentEntity].m_Vehicle : Entity.Null;

                // Log the current state periodically
                if (m_SimulationSystem.frameIndex % 64 == 0)
                {
                    bool hasPath = m_PathElementLookup.HasBuffer(residentEntity);
                    int pathLength = hasPath ? m_PathElementLookup[residentEntity].Length : 0;
                    Mod.log.Info($"[V2] Resident {residentEntity.Index}: state={forcedTrip.m_State}, target={target.m_Target.Index}, building={currentBuildingEntity.Index}, vehicle={currentVehicle.Index}, pathLen={pathLength}");
                }

                switch (forcedTrip.m_State)
                {
                    case ForcedStationTripState.WaitingAtStation:
                        // We're waiting at the station - check if we boarded a vehicle
                        if (inVehicle)
                        {
                            Mod.log.Info($"[V2] Resident {residentEntity.Index} boarded vehicle {currentVehicle.Index}!");
                            forcedTrip.m_State = ForcedStationTripState.OnVehicle;
                            forcedTrip.m_StartFrame = m_SimulationSystem.frameIndex;
                            EntityManager.SetComponentData(residentEntity, forcedTrip);
                        }
                        else
                        {
                            // Still waiting - check if the game reset our waiting state
                            // If so, we need to re-establish it
                            if (m_ResidentLookup.TryGetComponent(residentEntity, out var resident))
                            {
                                if ((resident.m_Flags & ResidentFlags.WaitingTransport) == 0)
                                {
                                    // Game cleared our waiting flag - maybe the bus passed?
                                    // Or the game decided we should take a different route
                                    // For now, just log and remove the forced trip
                                    uint framesSinceStart = m_SimulationSystem.frameIndex - forcedTrip.m_StartFrame;
                                    if (framesSinceStart > 300) // ~5 seconds
                                    {
                                        Mod.log.Warn($"[V2] Resident {residentEntity.Index} lost WaitingTransport flag after {framesSinceStart} frames, aborting forced trip");
                                        EntityManager.RemoveComponent<ForcedStationTrip>(residentEntity);
                                    }
                                }
                            }
                        }
                        break;

                    case ForcedStationTripState.OnVehicle:
                        // We're on the vehicle - check if we got off
                        if (!inVehicle)
                        {
                            Mod.log.Info($"[V2] Resident {residentEntity.Index} exited vehicle, now going to destination");
                            forcedTrip.m_State = ForcedStationTripState.GoingToDestination;
                            forcedTrip.m_StartFrame = m_SimulationSystem.frameIndex;
                            EntityManager.SetComponentData(residentEntity, forcedTrip);
                        }
                        break;

                    case ForcedStationTripState.GoingToStation:
                        // This state is used when the citizen is walking to a station
                        // Since we're teleporting them directly, this shouldn't be used normally
                        // But handle it for completeness - check if they reached the station
                        if (currentBuildingEntity == forcedTrip.m_CurrentStationTarget ||
                            IsAtStation(currentBuildingEntity, forcedTrip.m_CurrentStationTarget))
                        {
                            Mod.log.Info($"[V2] Resident {residentEntity.Index} reached station {forcedTrip.m_CurrentStationTarget.Index}");
                            forcedTrip.m_State = ForcedStationTripState.WaitingAtStation;
                            forcedTrip.m_StartFrame = m_SimulationSystem.frameIndex;
                            EntityManager.SetComponentData(residentEntity, forcedTrip);
                        }
                        break;

                    case ForcedStationTripState.GoingToDestination:
                        // Check if we've reached the final destination
                        if (currentBuildingEntity == forcedTrip.m_FinalDestination)
                        {
                            Mod.log.Info($"[V2] Resident {residentEntity.Index} reached final destination {forcedTrip.m_FinalDestination.Index}");
                            // Remove the forced trip component - journey complete!
                            EntityManager.RemoveComponent<ForcedStationTrip>(residentEntity);
                        }
                        break;
                }
            }

            entities.Dispose();
            forcedTrips.Dispose();
        }

        /// <summary>
        /// Check if the current building is at/connected to the target station
        /// </summary>
        private bool IsAtStation(Entity currentBuilding, Entity targetStation)
        {
            if (currentBuilding == Entity.Null || targetStation == Entity.Null)
                return false;

            // Direct match
            if (currentBuilding == targetStation)
                return true;

            // Check if current building is connected to the station somehow
            // This handles cases where the citizen is at a connected stop/platform
            // TODO: Add more sophisticated checking for connected stops

            return false;
        }

        /// <summary>
        /// Get the home entity for a citizen
        /// </summary>
        private Entity GetHomeEntity(Entity citizenEntity)
        {
            if (!m_HouseholdMemberLookup.TryGetComponent(citizenEntity, out var householdMember))
                return Entity.Null;
            
            Entity household = householdMember.m_Household;
            if (household == Entity.Null)
                return Entity.Null;
            
            if (!m_PropertyRenterLookup.TryGetComponent(household, out var propertyRenter))
                return Entity.Null;
            
            return propertyRenter.m_Property;
        }

        /// <summary>
        /// Find a rule that applies to the given home and workplace and has transport priorities
        /// </summary>
        private ResourceChainRule FindApplicableRuleWithPriorities(Entity homeEntity, Entity workplaceEntity)
        {
            var allConfigs = ResourceChainManagementSystem.GetActiveConfigurations();
            
            foreach (var kvp in allConfigs)
            {
                string configKey = kvp.Key;
                var config = kvp.Value;
                
                // The config key format is "building_{entityIndex}" - extract the entity index
                int configBuildingIndex = -1;
                if (configKey.StartsWith("building_"))
                {
                    int.TryParse(configKey.Substring(9), out configBuildingIndex);
                }
                
                foreach (var rule in config.Rules)
                {
                    // Only consider rules with transport priorities and Worker transport type
                    if (rule.TransportPriorities.Count == 0)
                        continue;
                    
                    if (rule.TransportType != Data.TransportType.Workers)
                        continue;

                    // Log the check we're about to perform
                    Mod.log.Info($"[V2] Checking rule {rule.Id} (config={configKey}, configBldg={configBuildingIndex}): Buildings=[{string.Join(",", rule.Buildings)}], home={homeEntity.Index}, workplace={workplaceEntity.Index}");

                    // Check if home matches this rule's buildings
                    if (rule.Buildings.Contains(homeEntity.Index))
                    {
                        Mod.log.Info($"[V2] Rule {rule.Id} matches HOME {homeEntity.Index}!");
                        return rule;
                    }
                    
                    // ALSO check if workplace matches (config might be set on workplace)
                    if (rule.Buildings.Contains(workplaceEntity.Index))
                    {
                        Mod.log.Info($"[V2] Rule {rule.Id} matches WORKPLACE {workplaceEntity.Index}!");
                        return rule;
                    }
                    
                    // ALSO check if the config building itself matches (config might be stored by building)
                    if (configBuildingIndex == homeEntity.Index || configBuildingIndex == workplaceEntity.Index)
                    {
                        Mod.log.Info($"[V2] Config building {configBuildingIndex} matches home or workplace!");
                        return rule;
                    }
                }
            }
            
            return null;
        }
    }

    /// <summary>
    /// Simple component to track forced station trips
    /// </summary>
    public struct ForcedStationTrip : IComponentData
    {
        public Entity m_FinalDestination;
        public Entity m_CurrentStationTarget;
        public int m_CurrentPriorityIndex;
        public int m_TotalPriorities;
        public ForcedStationTripState m_State;
        public uint m_StartFrame;  // Frame when the current leg started - used to avoid false arrival detection
        public bool m_PathWasSet;  // True once we've seen a non-empty path (means pathfinding completed)
    }

    /// <summary>
    /// States for the forced station trip
    /// </summary>
    public enum ForcedStationTripState
    {
        GoingToStation,      // Walking to the forced station waypoint
        WaitingAtStation,    // Waiting at the station for transport to arrive
        OnVehicle,           // Currently riding on the transport vehicle
        GoingToDestination   // Walking from station to final destination
    }
}
