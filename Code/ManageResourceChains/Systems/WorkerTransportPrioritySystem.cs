using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Jobs;
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
using Game.Areas;
using Game.Tools;
using Game.Vehicles;
using Game.Prefabs;
using Unity.Mathematics;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts worker commutes and forces them via prioritized transport stops.
    /// Implementation of Scenario 1 (Multi-stage trip) from the transport priority documentation.
    /// </summary>
    public partial class WorkerTransportPrioritySystem : GameSystemBase
    {
        private ResourceChainManagementSystem m_ResourceChainManagementSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_ResidentQuery;
        private EntityQuery m_ForcedTripQuery;
        private EntityQuery m_WaypointQuery;
        
        private ComponentLookup<CurrentDistrict> m_CurrentDistrictLookup;
        private ComponentLookup<PropertyRenter> m_PropertyRenterLookup;
        private ComponentLookup<HouseholdMember> m_HouseholdMemberLookup;
        private ComponentLookup<Worker> m_WorkerLookup;
        private ComponentLookup<Target> m_TargetLookup;
        private ComponentLookup<PathOwner> m_PathOwnerLookup;
        private ComponentLookup<TravelPurpose> m_TravelPurposeLookup;
        private ComponentLookup<HumanNavigation> m_HumanNavigationLookup;
        private ComponentLookup<HumanCurrentLane> m_HumanCurrentLaneLookup;
        private ComponentLookup<Waypoint> m_WaypointLookup;
        private ComponentLookup<Owner> m_OwnerLookup;
        private ComponentLookup<AccessLane> m_AccessLaneLookup;
        private ComponentLookup<Connected> m_ConnectedLookup;
        private ComponentLookup<WaitingPassengers> m_WaitingPassengersLookup;
        private ComponentLookup<BoardingVehicle> m_BoardingVehicleLookup;
        private ComponentLookup<CurrentRoute> m_CurrentRouteLookup;
        private ComponentLookup<Game.Vehicles.PublicTransport> m_PublicTransportLookup;
        private ComponentLookup<Game.Vehicles.Taxi> m_TaxiLookup;
        private ComponentLookup<PrefabRef> m_PrefabRefLookup;
        private ComponentLookup<ObjectGeometryData> m_ObjectGeometryDataLookup;
        private ComponentLookup<Game.Objects.Transform> m_TransformLookup;
        private BufferLookup<RouteWaypoint> m_RouteWaypointLookup;
        private BufferLookup<PathElement> m_PathElementLookup;
        private BufferLookup<ConnectedRoute> m_ConnectedRouteLookup;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            
            m_ResidentQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadOnly<Game.Creatures.Resident>(), 
                    ComponentType.ReadWrite<Target>(),
                    ComponentType.ReadWrite<PathOwner>()
                },
                None = new[] { 
                    ComponentType.ReadOnly<ForcedPriorityTrip>(), 
                    ComponentType.ReadOnly<Deleted>(), 
                    ComponentType.ReadOnly<Temp>() 
                }
            });
            
            m_ForcedTripQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadWrite<ForcedPriorityTrip>(),
                    ComponentType.ReadWrite<Target>(),
                    ComponentType.ReadWrite<PathOwner>(),
                    ComponentType.ReadOnly<Game.Creatures.Resident>(),
                    ComponentType.ReadWrite<HumanCurrentLane>()
                },
                None = new[] { 
                    ComponentType.ReadOnly<Deleted>(), 
                    ComponentType.ReadOnly<Temp>() 
                }
            });

            m_WaypointQuery = GetEntityQuery(new EntityQueryDesc
            {
                Any = new[] { 
                    ComponentType.ReadOnly<Waypoint>(), 
                    ComponentType.ReadOnly<Building>() 
                },
                None = new[] { 
                    ComponentType.ReadOnly<Deleted>(), 
                    ComponentType.ReadOnly<Temp>() 
                }
            });
            
            m_CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true);
            m_PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true);
            m_HouseholdMemberLookup = GetComponentLookup<HouseholdMember>(true);
            m_WorkerLookup = GetComponentLookup<Worker>(true);
            m_TargetLookup = GetComponentLookup<Target>(false);
            m_PathOwnerLookup = GetComponentLookup<PathOwner>(false);
            m_TravelPurposeLookup = GetComponentLookup<TravelPurpose>(true);
            m_HumanNavigationLookup = GetComponentLookup<HumanNavigation>(true);
            m_HumanCurrentLaneLookup = GetComponentLookup<HumanCurrentLane>(false);
            m_WaypointLookup = GetComponentLookup<Waypoint>(true);
            m_OwnerLookup = GetComponentLookup<Owner>(true);
            m_AccessLaneLookup = GetComponentLookup<AccessLane>(true);
            m_ConnectedLookup = GetComponentLookup<Connected>(true);
            m_WaitingPassengersLookup = GetComponentLookup<WaitingPassengers>(true);
            m_BoardingVehicleLookup = GetComponentLookup<BoardingVehicle>(true);
            m_CurrentRouteLookup = GetComponentLookup<CurrentRoute>(true);
            m_PublicTransportLookup = GetComponentLookup<Game.Vehicles.PublicTransport>(false);
            m_TaxiLookup = GetComponentLookup<Game.Vehicles.Taxi>(true);
            m_PrefabRefLookup = GetComponentLookup<PrefabRef>(true);
            m_ObjectGeometryDataLookup = GetComponentLookup<ObjectGeometryData>(true);
            m_TransformLookup = GetComponentLookup<Game.Objects.Transform>(true);
            m_RouteWaypointLookup = GetBufferLookup<RouteWaypoint>(true);
            m_PathElementLookup = GetBufferLookup<PathElement>(false);
            m_ConnectedRouteLookup = GetBufferLookup<ConnectedRoute>(true);
            
            Mod.log.Info("WorkerTransportPrioritySystem created");
        }

        protected override void OnUpdate()
        {
            m_CurrentDistrictLookup.Update(this);
            m_PropertyRenterLookup.Update(this);
            m_HouseholdMemberLookup.Update(this);
            m_WorkerLookup.Update(this);
            m_TargetLookup.Update(this);
            m_PathOwnerLookup.Update(this);
            m_TravelPurposeLookup.Update(this);
            m_HumanNavigationLookup.Update(this);
            m_HumanCurrentLaneLookup.Update(this);
            m_WaypointLookup.Update(this);
            m_OwnerLookup.Update(this);
            m_AccessLaneLookup.Update(this);
            m_ConnectedLookup.Update(this);
            m_WaitingPassengersLookup.Update(this);
            m_BoardingVehicleLookup.Update(this);
            m_CurrentRouteLookup.Update(this);
            m_PublicTransportLookup.Update(this);
            m_TaxiLookup.Update(this);
            m_PrefabRefLookup.Update(this);
            m_ObjectGeometryDataLookup.Update(this);
            m_TransformLookup.Update(this);
            m_RouteWaypointLookup.Update(this);
            m_PathElementLookup.Update(this);
            m_ConnectedRouteLookup.Update(this);

            InterceptNewTrips();
            AdvanceTrips();
        }

        private void InterceptNewTrips()
        {
            if (m_ResidentQuery.IsEmptyIgnoreFilter) return;

            var entities = m_ResidentQuery.ToEntityArray(Allocator.Temp);
            var residents = m_ResidentQuery.ToComponentDataArray<Game.Creatures.Resident>(Allocator.Temp);
            var targets = m_ResidentQuery.ToComponentDataArray<Target>(Allocator.Temp);
            
            var allConfigs = ResourceChainManagementSystem.GetActiveConfigurations();
            if (allConfigs.Count == 0) return;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity residentEntity = entities[i];
                Entity citizenEntity = residents[i].m_Citizen;

                if (!m_TravelPurposeLookup.TryGetComponent(citizenEntity, out var travelPurpose)) continue;
                if (travelPurpose.m_Purpose != Purpose.GoingToWork) continue;

                if (!m_WorkerLookup.TryGetComponent(citizenEntity, out var worker)) continue;
                
                // If the target is already the workplace, we can intercept
                if (targets[i].m_Target == worker.m_Workplace)
                {
                    Entity homeEntity = GetHomeEntity(citizenEntity);
                    if (homeEntity == Entity.Null) continue;

                    var rule = FindApplicableRuleWithPriorities(homeEntity, worker.m_Workplace);
                    if (rule != null && rule.TransportPriorities.Count > 0)
                    {
                        StartForcedTrip(residentEntity, worker.m_Workplace, rule.TransportPriorities);
                    }
                }
            }
        }

        private void AdvanceTrips()
        {
            if (m_ForcedTripQuery.IsEmptyIgnoreFilter) return;

            var entities = m_ForcedTripQuery.ToEntityArray(Allocator.Temp);
            var forcedTrips = m_ForcedTripQuery.ToComponentDataArray<ForcedPriorityTrip>(Allocator.Temp);
            var targets = m_ForcedTripQuery.ToComponentDataArray<Target>(Allocator.Temp);
            var pathOwners = m_ForcedTripQuery.ToComponentDataArray<PathOwner>(Allocator.Temp);
            var residents = m_ForcedTripQuery.ToComponentDataArray<Game.Creatures.Resident>(Allocator.Temp);
            var humanLanes = m_ForcedTripQuery.ToComponentDataArray<HumanCurrentLane>(Allocator.Temp);
            
            // Optimization: Only get waypoints if we actually need to resolve one
            NativeArray<Entity> waypoints = default;
            bool waypointsFetched = false;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity residentEntity = entities[i];
                ForcedPriorityTrip forcedTrip = forcedTrips[i];
                Target target = targets[i];
                PathOwner pathOwner = pathOwners[i];
                HumanCurrentLane humanLane = humanLanes[i];
                var resident = residents[i];
                Entity citizenEntity = resident.m_Citizen;

                bool inVehicle = (resident.m_Flags & ResidentFlags.InVehicle) != 0;
                bool hasVehicle = EntityManager.HasComponent<CurrentVehicle>(residentEntity);
                CurrentVehicle currentVehicle = hasVehicle ? EntityManager.GetComponentData<CurrentVehicle>(residentEntity) : default;
                
                // Use Ready flag to ensure boarding sequence is fully complete
                bool finishedEntering = hasVehicle && (currentVehicle.m_Flags & CreatureVehicleFlags.Ready) != 0;
                bool isEntering = hasVehicle && (currentVehicle.m_Flags & CreatureVehicleFlags.Ready) == 0;
                
                bool endReached = (humanLane.m_Flags & CreatureLaneFlags.EndReached) != 0;

                if (forcedTrip.m_State == ForcedTripState.MovingToPriority)
                {
                    if (endReached && !inVehicle && !hasVehicle)
                    {
                        // Reached a priority stop
                        Entity homeEntity = GetHomeEntity(citizenEntity);
                        var rule = FindApplicableRuleWithPriorities(homeEntity, forcedTrip.m_FinalTarget);
                        
                        if (rule != null && forcedTrip.m_NextPriorityIndex >= 0 && forcedTrip.m_NextPriorityIndex < rule.TransportPriorities.Count)
                        {
                            if (!waypointsFetched)
                            {
                                waypoints = m_WaypointQuery.ToEntityArray(Allocator.Temp);
                                waypointsFetched = true;
                            }

                            // Resolve boarding waypoint and stop
                            Entity priorityStation = FindEntityInArray(waypoints, rule.TransportPriorities[forcedTrip.m_NextPriorityIndex].StationEntity);
                            ResolveWaypoint(priorityStation, waypoints, out forcedTrip.m_CurrentResolvedWaypoint, out forcedTrip.m_CurrentResolvedStop);

                            // 1. Transition to waiting at this stop
                            forcedTrip.m_State = ForcedTripState.WaitingAtPriority;
                            
                            // 2. Set target to NEXT destination IMMEDIATELY
                            // This is crucial to avoid de-boarding immediately after entering
                            forcedTrip.m_NextPriorityIndex++;
                            if (forcedTrip.m_NextPriorityIndex < rule.TransportPriorities.Count)
                            {
                                var nextPriority = rule.TransportPriorities[forcedTrip.m_NextPriorityIndex];
                                Entity nextTarget = FindEntityInArray(waypoints, nextPriority.StationEntity);
                                if (nextTarget != Entity.Null)
                                {
                                    target.m_Target = nextTarget;
                                }
                                else
                                {
                                    target.m_Target = forcedTrip.m_FinalTarget;
                                }
                            }
                            else
                            {
                                target.m_Target = forcedTrip.m_FinalTarget;
                            }

                            // 3. Setup flags and path for native AI to take over queuing and boarding
                            pathOwner.m_State &= ~(PathFlags.Obsolete | PathFlags.Pending | PathFlags.Failed);
                            pathOwner.m_State |= PathFlags.Updated;
                            
                            humanLane.m_Flags |= CreatureLaneFlags.Transport | CreatureLaneFlags.EndReached;
                            resident.m_Flags |= ResidentFlags.WaitingTransport | ResidentFlags.NoLateDeparture;
                            resident.m_Flags &= ~(ResidentFlags.Arrived | ResidentFlags.Hangaround);
                            resident.m_Timer = 0;

                            Entity boardingWaypoint = forcedTrip.m_CurrentResolvedWaypoint;
                            Entity waitingStop = (forcedTrip.m_CurrentResolvedStop != Entity.Null) ? forcedTrip.m_CurrentResolvedStop : boardingWaypoint;

                            if (boardingWaypoint != Entity.Null)
                            {
                                // Set lane to the stop entity that has Connected component
                                humanLane.m_Lane = waitingStop;
                                
                                // Do NOT set m_CurvePosition here - let the native AI's SetQueuePosition handle it
                                // The native AI will use the entity's actual position to calculate queue positions
                                
                                // Find the destination waypoint (where we want to get OFF the bus)
                                // Use the new function that finds the waypoint closest to our target
                                Entity destinationWaypoint = FindDestinationWaypointOnLine(boardingWaypoint, target.m_Target);

                                if (destinationWaypoint != Entity.Null)
                                {
                                    // Set up path: [boarding waypoint, destination waypoint]
                                    // Only 2 elements! After CurrentVehicleBoarding does elementIndex += 2,
                                    // ExitVehicle will check: elementIndex < path.Length → 2 < 2 = FALSE
                                    // This forces ExitVehicle to use fallback behavior (vehicle position)
                                    // instead of trying to navigate to a building entity.
                                    var pathElements = EntityManager.GetBuffer<PathElement>(residentEntity);
                                    pathElements.Clear();
                                    pathElements.Add(new PathElement(boardingWaypoint, 0f));  // [0]: boarding point
                                    pathElements.Add(new PathElement(destinationWaypoint, 0f)); // [1]: exit waypoint (checked by ShouldExitVehicle)
                                    // DO NOT add element [2] - prevents ExitVehicle from using building as target!
                                    
                                    pathOwner.m_ElementIndex = 0;
                                }
                            }

                            EntityManager.SetComponentData(residentEntity, target);
                            EntityManager.SetComponentData(residentEntity, pathOwner);
                            EntityManager.SetComponentData(residentEntity, forcedTrip);
                            EntityManager.SetComponentData(residentEntity, humanLane);
                            EntityManager.SetComponentData(residentEntity, resident);
                            
                            Mod.log.Info($"Worker {citizenEntity.Index} reached stop {priorityStation.Index}. Waiting for transport to {target.m_Target.Index}. Resolved Stop: {waitingStop.Index}");
                        }
                        else
                        {
                            // Rule gone or priority list changed/invalid, end trip
                            Mod.log.Info($"Worker {citizenEntity.Index} forced trip invalid or completed. Index: {forcedTrip.m_NextPriorityIndex}, Rule: {rule != null}");
                            EndForcedTrip(residentEntity, forcedTrip.m_FinalTarget);
                        }
                    }
                }
                else if (forcedTrip.m_State == ForcedTripState.WaitingAtPriority)
                {
                    if (finishedEntering)
                    {
                        // Successfully boarded and finished the enter-vehicle sequence!
                        if (target.m_Target == forcedTrip.m_FinalTarget)
                        {
                            forcedTrip.m_State = ForcedTripState.MovingToFinalTarget;
                        }
                        else
                        {
                            forcedTrip.m_State = ForcedTripState.MovingToPriority;
                        }
                        
                        // Clear waiting flags but KEEP CannotIgnore to prevent premature de-boarding
                        resident.m_Flags &= ~(ResidentFlags.WaitingTransport | ResidentFlags.NoLateDeparture);
                        resident.m_Flags |= ResidentFlags.CannotIgnore;
                        
                        // Ensure lane is set to the vehicle and clear arrival flags
                        humanLane.m_Lane = currentVehicle.m_Vehicle;
                        humanLane.m_Flags &= ~(CreatureLaneFlags.EndReached | CreatureLaneFlags.EndOfPath | CreatureLaneFlags.Transport | CreatureLaneFlags.WaitPosition);
                        
                        // NATIVE AI PATH STRUCTURE FOR PUBLIC TRANSPORT:
                        // The native AI's CurrentVehicleBoarding checks:
                        // - pathElements[pathOwner.m_ElementIndex + 1].m_Target = destination waypoint to exit at
                        // - pathElements[pathOwner.m_ElementIndex + 2].m_Target = next lane/element AFTER exiting (for pathfinding)
                        // When deciding to exit: pathOwner.m_ElementIndex += 2 (skips boarding and destination waypoints)
                        //
                        // Path structure while on vehicle:
                        // [0] = boarding waypoint (where we got on)
                        // [1] = destination waypoint (where to get off) - this is checked by ShouldExitVehicle
                        // [2] = next target (where to walk after exiting) - needed for pathfinding after exit
                        //
                        // DO NOT add vehicle to path - the native AI doesn't expect it there!
                        
                        Entity boardingWaypoint = forcedTrip.m_CurrentResolvedWaypoint;
                        
                        // Find destination waypoint - use the function that finds the waypoint closest to target
                        Entity destinationWaypoint = FindDestinationWaypointOnLine(boardingWaypoint, target.m_Target);
                        
                        var pathElements = EntityManager.GetBuffer<PathElement>(residentEntity);
                        pathElements.Clear();
                        
                        // [0] Boarding waypoint (where we got on)
                        if (boardingWaypoint != Entity.Null)
                        {
                            pathElements.Add(new PathElement(boardingWaypoint, 0f));
                        }
                        
                        // [1] Destination waypoint (where to exit) - REQUIRED for exit check
                        if (destinationWaypoint != Entity.Null)
                        {
                            pathElements.Add(new PathElement(destinationWaypoint, 0f));
                        }
                        
                        // DO NOT add element [2]!
                        // After CurrentVehicleBoarding does elementIndex += 2, elementIndex = 2
                        // ExitVehicle checks: if (elementIndex < path.Length && !Obsolete)
                        // With 2 elements: 2 < 2 = FALSE, so ExitVehicle uses fallback (vehicle position)
                        // If we added the building as [2], ExitVehicle would use it as target = broken pathfinding!
                        
                        pathOwner.m_ElementIndex = 0;
                        pathOwner.m_State &= ~(PathFlags.Obsolete | PathFlags.Failed);
                        pathOwner.m_State |= PathFlags.Updated;
                        
                        EntityManager.SetComponentData(residentEntity, pathOwner);
                        EntityManager.SetComponentData(residentEntity, forcedTrip);
                        EntityManager.SetComponentData(residentEntity, resident);
                        EntityManager.SetComponentData(residentEntity, humanLane);
                        
                        Mod.log.Info($"Worker {citizenEntity.Index} successfully boarded! Path: [{boardingWaypoint.Index}, {destinationWaypoint.Index}] (2 elements only, target={target.m_Target.Index}) in vehicle {currentVehicle.m_Vehicle.Index}");
                    }
                    else if (isEntering)
                    {
                        // Currently in the middle of entering vehicle. 
                        // Allow native AI to handle the flags and animation.
                    }
                    else
                    {
                        // Still waiting at the stop. Reinforce state so AI doesn't give up.
                        bool changed = false;
                        if ((humanLane.m_Flags & CreatureLaneFlags.Transport) == 0) { humanLane.m_Flags |= CreatureLaneFlags.Transport; changed = true; }
                        if ((humanLane.m_Flags & CreatureLaneFlags.EndReached) == 0) { humanLane.m_Flags |= CreatureLaneFlags.EndReached; changed = true; }
                        // Don't set WaitPosition flag - let native AI handle queuing position
                        if ((resident.m_Flags & ResidentFlags.WaitingTransport) == 0) { resident.m_Flags |= ResidentFlags.WaitingTransport; changed = true; }
                        
                        // Prevent giving up on transport
                        if ((resident.m_Flags & ResidentFlags.CannotIgnore) == 0) { resident.m_Flags |= ResidentFlags.CannotIgnore; changed = true; }
                        if (resident.m_Timer > 4000) { resident.m_Timer = 0; changed = true; }

                        Entity boardingWaypoint = forcedTrip.m_CurrentResolvedWaypoint;
                        if (boardingWaypoint != Entity.Null)
                        {
                            // Find the destination waypoint (where we want to exit the bus)
                            Entity destinationWaypoint = FindDestinationWaypointOnLine(boardingWaypoint, target.m_Target);

                            if (destinationWaypoint != Entity.Null)
                            {
                                var pathElements = EntityManager.GetBuffer<PathElement>(residentEntity);
                                // Force path ONLY if it has been cleared or invalidated by native AI
                                if (pathElements.Length < 2)
                                {
                                    pathElements.Clear();
                                    pathElements.Add(new PathElement(boardingWaypoint, 0f));
                                    pathElements.Add(new PathElement(destinationWaypoint, 0f));
                                    // DO NOT add element [2] - prevents ExitVehicle from using building as target!
                                    
                                    pathOwner.m_ElementIndex = 0;
                                    pathOwner.m_State |= PathFlags.Updated;
                                    pathOwner.m_State &= ~PathFlags.Obsolete;
                                    changed = true;
                                }
                                
                                // MANUALLY CHECK FOR BOARDING VEHICLE AND SIGNAL IT TO STOP
                                uint minDeparture = ((resident.m_Flags & ResidentFlags.NoLateDeparture) != 0) ? m_SimulationSystem.frameIndex : 0u;
                                if (RouteUtils.GetBoardingVehicle(humanLane.m_Lane, boardingWaypoint, destinationWaypoint, minDeparture, 
                                    ref m_OwnerLookup, ref m_TargetLookup, ref m_ConnectedLookup, ref m_BoardingVehicleLookup, 
                                    ref m_CurrentRouteLookup, ref m_AccessLaneLookup, ref m_PublicTransportLookup, ref m_TaxiLookup, 
                                    ref m_ConnectedRouteLookup, ref m_RouteWaypointLookup, out var vehicle, out var testing, out var obsolete))
                                {
                                    // Boarding in progress
                                }
                                else if (!obsolete && testing && vehicle != Entity.Null)
                                {
                                    // Vehicle is approaching! SET REQUIRE STOP.
                                    if (m_PublicTransportLookup.TryGetComponent(vehicle, out var publicTransport))
                                    {
                                        if ((publicTransport.m_State & PublicTransportFlags.RequireStop) == 0)
                                        {
                                            publicTransport.m_State |= PublicTransportFlags.RequireStop;
                                            EntityManager.SetComponentData(vehicle, publicTransport);
                                            Mod.log.Info($"Worker {citizenEntity.Index} signaled vehicle {vehicle.Index} to stop at {boardingWaypoint.Index}");
                                        }
                                    }
                                }
                            }
                        }
                        
                        if (changed)
                        {
                            EntityManager.SetComponentData(residentEntity, pathOwner);
                            EntityManager.SetComponentData(residentEntity, humanLane);
                            EntityManager.SetComponentData(residentEntity, resident);
                        }
                    }
                }
                else if (forcedTrip.m_State == ForcedTripState.MovingToFinalTarget || forcedTrip.m_State == ForcedTripState.MovingToPriority)
                {
                    // CRITICAL: With 2-element path, native AI's CurrentVehicleBoarding does NOT mark path as Obsolete
                    // (because obsolete = false when nextLane = Entity.Null).
                    // We MUST manually mark the path as Obsolete BEFORE the passenger exits,
                    // or they'll try to use the stale 2-element path after exiting.
                    
                    bool isDisembarking = (resident.m_Flags & ResidentFlags.Disembarking) != 0;
                    
                    if (isDisembarking && hasVehicle)
                    {
                        // Still have CurrentVehicle but Disembarking flag set
                        // This means CurrentVehicleBoarding has run and set Disembarking
                        // Path is at elementIndex=2 (after += 2), but NOT marked Obsolete!
                        
                        // Mark path as Obsolete NOW so ExitVehicle and post-exit pathfinding work correctly
                        pathOwner.m_State |= PathFlags.Obsolete;
                        EntityManager.SetComponentData(residentEntity, pathOwner);
                        
                        Mod.log.Info($"Worker {citizenEntity.Index} DISEMBARKING detected - marked path Obsolete manually (native AI didn't do it). ElementIndex={pathOwner.m_ElementIndex}");
                        
                        continue; // Skip rest of processing this frame
                    }
                    
                    // Check if passenger has exited the vehicle
                    // We check !hasVehicle && !inVehicle to detect they are on foot
                    if (!hasVehicle && !inVehicle)
                    {
                        var pathElements = EntityManager.GetBuffer<PathElement>(residentEntity);
                        bool isObsolete = (pathOwner.m_State & PathFlags.Obsolete) != 0;
                        bool isPending = (pathOwner.m_State & PathFlags.Pending) != 0;
                        bool isUpdated = (pathOwner.m_State & PathFlags.Updated) != 0;
                        bool isFailed = (pathOwner.m_State & PathFlags.Failed) != 0;
                        bool disembarking = (resident.m_Flags & ResidentFlags.Disembarking) != 0;
                        
                        // CRITICAL: If elementIndex is at 2 or beyond, they've exited and are now pointing at element[2] (building entity)
                        // This element has NO lane information and causes them to walk to edge of map!
                        // We must IMMEDIATELY clear the path and request a new one.
                        if (pathOwner.m_ElementIndex >= 2)
                        {
                            // EMERGENCY: Immediately clear the path buffer to prevent using element[2]
                            // Do this BEFORE checking isPending/isUpdated to ensure we never use the invalid path
                            if (pathElements.Length > 0)
                            {
                                pathElements.Clear();
                                pathOwner.m_ElementIndex = 0;
                                pathOwner.m_State |= PathFlags.Obsolete;
                                EntityManager.SetComponentData(residentEntity, pathOwner);
                                Mod.log.Info($"Worker {citizenEntity.Index} EXITED vehicle - CLEARED path immediately to prevent edge-of-map walking!");
                            }
                            
                            // Now request proper pathfinding if not already in progress
                            if (!isPending && !isUpdated)
                            {
                                // Immediately end forced trip which will call PrepareWalkingSegment
                                if (forcedTrip.m_State == ForcedTripState.MovingToFinalTarget)
                                {
                                    EndForcedTrip(residentEntity, forcedTrip.m_FinalTarget);
                                }
                                else
                                {
                                    PrepareWalkingSegment(residentEntity, target.m_Target);
                                }
                            }
                            continue; // Skip rest of processing for this entity this frame
                        }
                        
                        // Regular exit detection logic
                        // Path is considered "finished" if we are at the end of the buffer or buffer is empty
                        bool pathAtEnd = pathOwner.m_ElementIndex >= pathElements.Length;

                        // We need a reset if:
                        // 1. Native AI says we reached the end of current lane/path (endReached)
                        // 2. Native AI marked the path as Obsolete or Failed
                        // 3. Mod/Native AI says we are Disembarking
                        // 4. We are at the end of the path buffer
                        // AND no pathfind is currently in progress (Pending or Updated)
                        bool needsPathReset = (endReached || isObsolete || isFailed || disembarking || pathAtEnd) && !isPending && !isUpdated;

                        if (needsPathReset)
                        {
                            // If this was the final stop, end the forced trip
                            if (forcedTrip.m_State == ForcedTripState.MovingToFinalTarget)
                            {
                                Mod.log.Info($"Worker {citizenEntity.Index} needs walking reset to FINAL workplace {forcedTrip.m_FinalTarget.Index}. Reason: EndReached={endReached}, Obsolete={isObsolete}, Failed={isFailed}, Disembarking={disembarking}, AtEnd={pathAtEnd}");
                                EndForcedTrip(residentEntity, forcedTrip.m_FinalTarget);
                            }
                            else if (forcedTrip.m_NextPriorityIndex > 0)
                            {
                                // Moving to another priority stop (and we've already completed at least one leg)
                                forcedTrip.m_State = ForcedTripState.MovingToPriority;
                                EntityManager.SetComponentData(residentEntity, forcedTrip);
                                
                                Mod.log.Info($"Worker {citizenEntity.Index} needs walking reset to NEXT priority {target.m_Target.Index}. Reason: EndReached={endReached}, Obsolete={isObsolete}, Failed={isFailed}, Disembarking={disembarking}, AtEnd={pathAtEnd}");
                                
                                // target.m_Target was already set to nextTarget when boarding
                                PrepareWalkingSegment(residentEntity, target.m_Target);
                            }
                        }
                    }
                }
            }
            
            if (waypointsFetched)
                waypoints.Dispose();
        }

        private void ResolveWaypoint(Entity entity, NativeArray<Entity> waypoints, out Entity waypoint, out Entity stop)
        {
            waypoint = Entity.Null;
            stop = Entity.Null;
            
            if (entity == Entity.Null) return;
            
            if (m_WaypointLookup.HasComponent(entity))
            {
                waypoint = entity;
                if (m_ConnectedLookup.TryGetComponent(entity, out var connected))
                {
                    Entity connectedEntity = connected.m_Connected;
                    if (m_AccessLaneLookup.TryGetComponent(connectedEntity, out var accessLane) && accessLane.m_Lane != Entity.Null)
                    {
                        stop = accessLane.m_Lane;
                    }
                    else
                    {
                        stop = connectedEntity;
                    }
                }
                return;
            }
            
            if (EntityManager.HasComponent<Building>(entity))
            {
                // Find a waypoint connected to a stop owned by this building
                foreach (var e in waypoints)
                {
                    if (m_WaypointLookup.HasComponent(e) && m_ConnectedLookup.TryGetComponent(e, out var connected))
                    {
                        Entity stopEntity = connected.m_Connected;
                        if (m_OwnerLookup.TryGetComponent(stopEntity, out var stopOwner) && stopOwner.m_Owner == entity)
                        {
                            waypoint = e;
                            if (m_AccessLaneLookup.TryGetComponent(stopEntity, out var accessLane) && accessLane.m_Lane != Entity.Null)
                            {
                                stop = accessLane.m_Lane;
                            }
                            else
                            {
                                stop = stopEntity;
                            }
                            return;
                        }
                    }
                }
            }
        }

        private Entity FindNextWaypointOnLine(Entity currentWaypoint)
        {
            if (!m_OwnerLookup.TryGetComponent(currentWaypoint, out var owner)) return Entity.Null;
            
            Entity lineEntity = owner.m_Owner;
            if (!m_RouteWaypointLookup.HasBuffer(lineEntity)) return Entity.Null;
            
            var waypoints = m_RouteWaypointLookup[lineEntity];
            if (waypoints.Length <= 1) return Entity.Null;

            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i].m_Waypoint == currentWaypoint)
                {
                    return waypoints[(i + 1) % waypoints.Length].m_Waypoint;
                }
            }
            
            return waypoints[0].m_Waypoint;
        }

        /// <summary>
        /// Find a waypoint on the same line as currentWaypoint that is closest to the target building.
        /// If target is already a waypoint, returns it directly.
        /// Falls back to the next waypoint on the line if no better match is found.
        /// </summary>
        private Entity FindDestinationWaypointOnLine(Entity currentWaypoint, Entity target)
        {
            // If target is already a waypoint, return it
            if (m_WaypointLookup.HasComponent(target))
            {
                return target;
            }
            
            if (!m_OwnerLookup.TryGetComponent(currentWaypoint, out var owner)) 
                return FindNextWaypointOnLine(currentWaypoint);
            
            Entity lineEntity = owner.m_Owner;
            if (!m_RouteWaypointLookup.HasBuffer(lineEntity)) 
                return FindNextWaypointOnLine(currentWaypoint);
            
            var waypoints = m_RouteWaypointLookup[lineEntity];
            if (waypoints.Length <= 1) 
                return FindNextWaypointOnLine(currentWaypoint);

            // Get target position
            if (!m_TransformLookup.TryGetComponent(target, out var targetTransform))
                return FindNextWaypointOnLine(currentWaypoint);
            
            float3 targetPos = targetTransform.m_Position;
            
            // Find the waypoint on this line that is closest to the target
            Entity bestWaypoint = Entity.Null;
            float bestDistance = float.MaxValue;
            int currentIndex = -1;
            
            // First find current waypoint index
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i].m_Waypoint == currentWaypoint)
                {
                    currentIndex = i;
                    break;
                }
            }
            
            if (currentIndex == -1)
                return FindNextWaypointOnLine(currentWaypoint);
            
            // Search waypoints AFTER current one on the route
            for (int offset = 1; offset < waypoints.Length; offset++)
            {
                int idx = (currentIndex + offset) % waypoints.Length;
                Entity waypointEntity = waypoints[idx].m_Waypoint;
                
                // Get waypoint position via its connected stop
                if (m_ConnectedLookup.TryGetComponent(waypointEntity, out var connected))
                {
                    if (m_TransformLookup.TryGetComponent(connected.m_Connected, out var waypointTransform))
                    {
                        float distance = math.distance(waypointTransform.m_Position, targetPos);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestWaypoint = waypointEntity;
                        }
                    }
                }
                else if (m_TransformLookup.TryGetComponent(waypointEntity, out var waypointTransform))
                {
                    float distance = math.distance(waypointTransform.m_Position, targetPos);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestWaypoint = waypointEntity;
                    }
                }
            }
            
            // If we found a good waypoint and it's reasonably close (within 500m), use it
            // Otherwise fall back to next waypoint
            if (bestWaypoint != Entity.Null && bestDistance < 500f)
            {
                return bestWaypoint;
            }
            
            return FindNextWaypointOnLine(currentWaypoint);
        }

        private void StartForcedTrip(Entity residentEntity, Entity workplace, List<TransportPriority> priorities)
        {
            var firstPriority = priorities[0];
            Entity firstTarget = FindEntityByIndex(firstPriority.StationEntity);

            if (firstTarget != Entity.Null)
            {
                PrepareWalkingSegment(residentEntity, firstTarget);

                EntityManager.AddComponentData(residentEntity, new ForcedPriorityTrip
                {
                    m_FinalTarget = workplace,
                    m_CurrentResolvedWaypoint = Entity.Null,
                    m_CurrentResolvedStop = Entity.Null,
                    m_NextPriorityIndex = 0,
                    m_State = ForcedTripState.MovingToPriority
                });

                Mod.log.Info($"Intercepted worker trip! Redirecting to priority {firstTarget.Index}");
            }
        }

        private void PrepareWalkingSegment(Entity residentEntity, Entity targetEntity)
        {
            Target target = EntityManager.GetComponentData<Target>(residentEntity);
            target.m_Target = targetEntity;
            EntityManager.SetComponentData(residentEntity, target);

            PathOwner pathOwner = EntityManager.GetComponentData<PathOwner>(residentEntity);
            // Clear Failed and Pending to ensure a fresh search starts
            pathOwner.m_State &= ~(PathFlags.Failed | PathFlags.Pending);
            pathOwner.m_State |= PathFlags.Obsolete | PathFlags.Updated;
            pathOwner.m_ElementIndex = 0;
            EntityManager.SetComponentData(residentEntity, pathOwner);
            
            if (EntityManager.HasBuffer<PathElement>(residentEntity))
            {
                EntityManager.GetBuffer<PathElement>(residentEntity).Clear();
            }
            
            if (EntityManager.HasComponent<HumanCurrentLane>(residentEntity))
            {
                var humanLane = EntityManager.GetComponentData<HumanCurrentLane>(residentEntity);
                // Clear all pathing/arrival flags and force FindLane
                humanLane.m_Flags &= ~(CreatureLaneFlags.EndReached | CreatureLaneFlags.EndOfPath | CreatureLaneFlags.Transport | CreatureLaneFlags.WaitPosition | CreatureLaneFlags.Stuck | CreatureLaneFlags.Obsolete);
                humanLane.m_Flags |= CreatureLaneFlags.FindLane;
                humanLane.m_QueueEntity = Entity.Null;
                EntityManager.SetComponentData(residentEntity, humanLane);
            }
            
            var resident = EntityManager.GetComponentData<Game.Creatures.Resident>(residentEntity);
            // Clear all behavioral and state flags that might interfere with clean pathfinding
            resident.m_Flags &= ~(ResidentFlags.Arrived | ResidentFlags.Hangaround | ResidentFlags.WaitingTransport | ResidentFlags.CannotIgnore | ResidentFlags.Disembarking | ResidentFlags.NoLateDeparture);
            EntityManager.SetComponentData(residentEntity, resident);
        }

        private void EndForcedTrip(Entity residentEntity, Entity finalTarget)
        {
            PrepareWalkingSegment(residentEntity, finalTarget);
            EntityManager.RemoveComponent<ForcedPriorityTrip>(residentEntity);
            
            Mod.log.Info($"Forced trip ended. Redirecting worker {residentEntity.Index} to final destination {finalTarget.Index}");
        }

        private Entity GetHomeEntity(Entity citizen)
        {
            if (m_HouseholdMemberLookup.TryGetComponent(citizen, out var householdMember))
            {
                if (m_PropertyRenterLookup.TryGetComponent(householdMember.m_Household, out var propertyRenter))
                {
                    return propertyRenter.m_Property;
                }
            }
            return Entity.Null;
        }

        private ResourceChainRule FindApplicableRuleWithPriorities(Entity home, Entity workplace)
        {
            var allConfigs = ResourceChainManagementSystem.GetActiveConfigurations();
            
            // Check building-level rules for home
            if (allConfigs.TryGetValue(ResourceChainManagementSystem.GetConfigKey(home.Index, EntityType.Building), out var homeConfig))
            {
                foreach (var rule in homeConfig.Rules)
                {
                    if (rule.TransportType == ManageResourceChains.Data.TransportType.Workers && rule.Type == ChainType.Outgoing)
                    {
                        if (IsTargetInRule(rule, workplace))
                        {
                            if (rule.TransportPriorities != null && rule.TransportPriorities.Count > 0)
                                return rule;
                        }
                    }
                }
            }

            // Check building-level rules for workplace
            if (allConfigs.TryGetValue(ResourceChainManagementSystem.GetConfigKey(workplace.Index, EntityType.Building), out var workplaceConfig))
            {
                foreach (var rule in workplaceConfig.Rules)
                {
                    if (rule.TransportType == ManageResourceChains.Data.TransportType.Workers && rule.Type == ChainType.Incoming)
                    {
                        if (IsTargetInRule(rule, home))
                        {
                            if (rule.TransportPriorities != null && rule.TransportPriorities.Count > 0)
                                return rule;
                        }
                    }
                }
            }

            // Check district rules ... (simplified for now, can add later)
            
            return null;
        }

        private bool IsTargetInRule(ResourceChainRule rule, Entity target)
        {
            if (rule.Buildings.Contains(target.Index)) return true;
            
            if (rule.Districts.Count > 0 && m_CurrentDistrictLookup.TryGetComponent(target, out var currentDistrict))
            {
                if (rule.Districts.Contains(currentDistrict.m_District.Index)) return true;
            }
            
            return false;
        }

        private Entity FindEntityInArray(NativeArray<Entity> entities, int index)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i].Index == index) return entities[i];
            }
            return Entity.Null;
        }

        private Entity FindEntityByIndex(int index)
        {
            // This is a bit slow but robust enough for a few calls per commute start
            var entities = m_WaypointQuery.ToEntityArray(Allocator.Temp);
            try {
                return FindEntityInArray(entities, index);
            } finally {
                entities.Dispose();
            }
        }
    }
}
