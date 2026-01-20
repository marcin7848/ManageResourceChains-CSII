﻿using Colossal.Entities;
using Colossal.Logging;
using Game.Buildings;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Game.Routes;
using ManageResourceChains;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// A tool for picking transport priorities (stations, stops, etc.)
    /// </summary>
    public partial class PriorityPickerToolSystem : ToolBaseSystem
    {
        private ILog m_Log;
        private Entity m_PreviousRaycastedEntity;
        private EntityQuery m_HighlightedQuery;
        private ToolOutputBarrier m_Barrier;
        private ResourceChainManagementSystem m_ResourceChainManagementSystem;

        /// <inheritdoc/>
        public override string toolID => "PriorityPickerTool";

        /// <inheritdoc/>
        public override PrefabBase GetPrefab()
        {
            return null;
        }

        /// <inheritdoc/>
        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }

        /// <inheritdoc/>
        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            
            // Allow picking buildings (stations/terminals) AND route waypoints (for lines)
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.RouteWaypoints | TypeMask.Net;
            
            // IMPORTANT: Do NOT use NoMainElements - that prevents buildings from being hit!
            // We need to hit buildings directly for stations that don't have any lines yet
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubBuildings | RaycastFlags.Markers | 
                                                RaycastFlags.Passenger | RaycastFlags.Cargo | RaycastFlags.BuildingLots;
            
            // Set route type and net layers to detect transport stops
            m_ToolRaycastSystem.routeType = Game.Routes.RouteType.TransportLine;
            m_ToolRaycastSystem.netLayerMask = Layer.Road | Layer.Pathway | Layer.MarkerPathway | Layer.PublicTransportRoad | 
                                              Layer.TrainTrack | Layer.TramTrack | Layer.SubwayTrack;
            
            m_Log.Info($"InitializeRaycast called: typeMask={m_ToolRaycastSystem.typeMask}, routeType={m_ToolRaycastSystem.routeType}, collisionMask={m_ToolRaycastSystem.collisionMask}, netLayerMask={m_ToolRaycastSystem.netLayerMask}, raycastFlags={m_ToolRaycastSystem.raycastFlags}");
        }

        /// <summary>
        /// Request to disable the tool and confirm selection.
        /// </summary>
        public void ConfirmSelection()
        {
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        /// <summary>
        /// Request to disable the tool and cancel selection.
        /// </summary>
        public void CancelSelection()
        {
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_Log = Mod.Log;
            m_Log.Info($"{nameof(PriorityPickerToolSystem)}.{nameof(OnCreate)}");
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            
            m_HighlightedQuery = SystemAPI.QueryBuilder()
                .WithAll<Highlighted>()
                .WithNone<Deleted, Temp, Overridden>()
                .Build();
        }

        /// <inheritdoc/>
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyAction.shouldBeEnabled = true;
            m_Log.Info($"{nameof(PriorityPickerToolSystem)} started - ready to pick transport priorities");
        }

        /// <inheritdoc/>
        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            EntityManager.AddComponent<BatchesUpdated>(m_HighlightedQuery);
            EntityManager.RemoveComponent<Highlighted>(m_HighlightedQuery);
            m_PreviousRaycastedEntity = Entity.Null;
            m_Log.Debug($"{nameof(PriorityPickerToolSystem)}.{nameof(OnStopRunning)}");
        }

        /// <inheritdoc/>
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            inputDeps = Dependency;
            EntityCommandBuffer buffer = m_Barrier.CreateCommandBuffer();

            if (cancelAction.WasPressedThisFrame())
            {
                m_ResourceChainManagementSystem.CancelPriorityPicker();
                return inputDeps;
            }

            if (!GetRaycastResult(out Entity currentRaycastEntity, out RaycastHit hit))
            {
                buffer.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                buffer.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                m_PreviousRaycastedEntity = Entity.Null;
                return inputDeps;
            }

            // If we hit a Route (via RouteWaypoints), resolve the actual waypoint entity
            if (EntityManager.HasComponent<Route>(currentRaycastEntity) && hit.m_CellIndex.x != -1)
            {
                if (EntityManager.HasBuffer<RouteWaypoint>(currentRaycastEntity))
                {
                    DynamicBuffer<RouteWaypoint> waypoints = EntityManager.GetBuffer<RouteWaypoint>(currentRaycastEntity);
                    if (hit.m_CellIndex.x >= 0 && hit.m_CellIndex.x < waypoints.Length)
                    {
                        Entity waypointEntity = waypoints[hit.m_CellIndex.x].m_Waypoint;
                        if (waypointEntity != Entity.Null)
                        {
                            if (currentRaycastEntity != m_PreviousRaycastedEntity)
                            {
                                m_Log.Info($"Resolved Route {currentRaycastEntity.Index} waypoint index {hit.m_CellIndex.x} to entity {waypointEntity.Index}");
                            }
                            currentRaycastEntity = waypointEntity;
                        }
                    }
                }
            }

            // Log what we're hitting
            if (currentRaycastEntity != m_PreviousRaycastedEntity)
            {
                m_Log.Info($"Raycast hit entity {currentRaycastEntity.Index} (v{currentRaycastEntity.Version})");
                
                // Check what components this entity has
                bool hasBuilding = EntityManager.HasComponent<Building>(currentRaycastEntity);
                bool hasWaypoint = EntityManager.HasComponent<Waypoint>(currentRaycastEntity);
                bool hasTransportStop = EntityManager.HasComponent<Game.Routes.TransportStop>(currentRaycastEntity);
                bool hasRoute = EntityManager.HasComponent<Route>(currentRaycastEntity);
                bool hasOwner = EntityManager.HasComponent<Owner>(currentRaycastEntity);
                bool hasPrefabRef = EntityManager.HasComponent<PrefabRef>(currentRaycastEntity);
                
                m_Log.Info($"  Components: Building={hasBuilding}, Waypoint={hasWaypoint}, TransportStop={hasTransportStop}, Route={hasRoute}, Owner={hasOwner}, PrefabRef={hasPrefabRef}");
                
                // If it has PrefabRef, log the prefab info
                if (hasPrefabRef)
                {
                    var prefabRef = EntityManager.GetComponentData<PrefabRef>(currentRaycastEntity);
                    m_Log.Info($"  Prefab entity: {prefabRef.m_Prefab.Index}");
                }
            }

            // Check if it's a valid transport priority target
            // Valid targets:
            // - Building that is a transport station (has TransportStation component)
            // - Waypoint (belongs to a transport line)
            // - TransportStop (the stop entity itself, for roadside stops)
            bool isValidBuilding = EntityManager.HasComponent<Building>(currentRaycastEntity) && 
                                   IsTransportStationBuilding(currentRaycastEntity);
            bool isValid = isValidBuilding || 
                           EntityManager.HasComponent<Waypoint>(currentRaycastEntity) ||
                           EntityManager.HasComponent<Game.Routes.TransportStop>(currentRaycastEntity);

            if (!isValid)
            {
                if (currentRaycastEntity != m_PreviousRaycastedEntity)
                {
                    m_Log.Info($"  Entity is NOT valid for priority picking");
                }
                buffer.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                buffer.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                m_PreviousRaycastedEntity = Entity.Null;
                return inputDeps;
            }

            if (currentRaycastEntity != m_PreviousRaycastedEntity)
            {
                m_Log.Info($"  Entity IS VALID for priority picking!");
            }

            // Resolve to station building for highlighting (so the whole station lights up)
            Entity entityToHighlight = ResolveToStationBuilding(currentRaycastEntity);

            // Update highlighting
            if (entityToHighlight != m_PreviousRaycastedEntity)
            {
                m_PreviousRaycastedEntity = entityToHighlight;
                NativeArray<Entity> entities = m_HighlightedQuery.ToEntityArray(Allocator.Temp);
                buffer.AddComponent<BatchesUpdated>(entities);
                buffer.RemoveComponent<Highlighted>(entities);
            }

            if (m_HighlightedQuery.IsEmptyIgnoreFilter)
            {
                buffer.AddComponent<BatchesUpdated>(entityToHighlight);
                buffer.AddComponent<Highlighted>(entityToHighlight);
                m_PreviousRaycastedEntity = entityToHighlight;
            }

            // Check for mouse click
            if (applyAction.WasReleasedThisFrame())
            {
                // Use the already-resolved station building
                m_Log.Info($"Apply action pressed! Raw entity: {currentRaycastEntity.Index}, Resolved to station: {entityToHighlight.Index}");
                m_ResourceChainManagementSystem.OnPrioritySelected(entityToHighlight);
            }

            return inputDeps;
        }

        /// <summary>
        /// Resolves a waypoint or stop entity to its owner station building if one exists.
        /// If no building owner exists (e.g., roadside bus stops), returns the stop entity itself.
        /// 
        /// This handles two cases:
        /// 1. Station buildings (train stations, bus terminals) - resolve to the building
        /// 2. Roadside stops (bus stops on roads) - return the stop entity directly
        /// </summary>
        private Entity ResolveToStationBuilding(Entity entity)
        {
            if (entity == Entity.Null)
                return entity;

            // If it's already a building, return it directly
            if (EntityManager.HasComponent<Building>(entity))
            {
                m_Log.Info($"Entity {entity.Index} is already a Building, using directly");
                return entity;
            }

            // If it's a waypoint, try to find the stop's owner building
            if (EntityManager.HasComponent<Waypoint>(entity))
            {
                // First, get the connected stop entity
                if (EntityManager.HasComponent<Connected>(entity))
                {
                    var connected = EntityManager.GetComponentData<Connected>(entity);
                    Entity stopEntity = connected.m_Connected;
                    
                    m_Log.Info($"Waypoint {entity.Index} is connected to stop {stopEntity.Index}");
                    
                    // Get the stop's owner
                    if (EntityManager.HasComponent<Owner>(stopEntity))
                    {
                        var stopOwner = EntityManager.GetComponentData<Owner>(stopEntity);
                        Entity ownerEntity = stopOwner.m_Owner;
                        
                        // Check if owner is a building (station)
                        if (EntityManager.HasComponent<Building>(ownerEntity))
                        {
                            m_Log.Info($"Resolved waypoint {entity.Index} -> stop {stopEntity.Index} -> station building {ownerEntity.Index}");
                            return ownerEntity;
                        }
                        else
                        {
                            // Owner is NOT a building (likely a road segment for roadside stops)
                            // Return the STOP entity itself - this is a standalone stop
                            m_Log.Info($"Stop {stopEntity.Index} owner {ownerEntity.Index} is not a building (roadside stop). Using stop entity.");
                            return stopEntity;
                        }
                    }
                    else
                    {
                        // Stop has no owner - return the stop entity itself
                        m_Log.Info($"Stop {stopEntity.Index} has no owner. Using stop entity directly.");
                        return stopEntity;
                    }
                }
                else
                {
                    // Waypoint has no Connected component - this shouldn't happen for transport stops
                    m_Log.Info($"Waypoint {entity.Index} has no Connected component. Using waypoint directly.");
                    return entity;
                }
            }

            // If it's a transport stop directly (not via waypoint), check its owner
            if (EntityManager.HasComponent<Game.Routes.TransportStop>(entity))
            {
                if (EntityManager.HasComponent<Owner>(entity))
                {
                    var owner = EntityManager.GetComponentData<Owner>(entity);
                    Entity ownerEntity = owner.m_Owner;
                    
                    if (EntityManager.HasComponent<Building>(ownerEntity))
                    {
                        m_Log.Info($"Resolved stop {entity.Index} -> station building {ownerEntity.Index}");
                        return ownerEntity;
                    }
                    else
                    {
                        // Not a building owner (roadside stop) - return the stop itself
                        m_Log.Info($"Stop {entity.Index} owner {ownerEntity.Index} is not a building (roadside stop). Using stop entity.");
                        return entity;
                    }
                }
                else
                {
                    m_Log.Info($"Stop {entity.Index} has no owner. Using stop entity directly.");
                    return entity;
                }
            }

            // Could not resolve - return the original entity
            m_Log.Info($"Entity {entity.Index} is not a building, waypoint, or stop. Using original.");
            return entity;
        }

        /// <summary>
        /// Checks if a building is a transport station (bus station, train station, etc.)
        /// by checking its prefab for TransportStation-related components.
        /// </summary>
        private bool IsTransportStationBuilding(Entity buildingEntity)
        {
            if (!EntityManager.HasComponent<PrefabRef>(buildingEntity))
                return false;
            
            var prefabRef = EntityManager.GetComponentData<PrefabRef>(buildingEntity);
            Entity prefab = prefabRef.m_Prefab;
            
            // Check for various transport station prefab components
            // TransportStationData indicates a public transport station (bus, train, tram, etc.)
            if (EntityManager.HasComponent<Game.Prefabs.TransportStationData>(prefab))
            {
                m_Log.Info($"Building {buildingEntity.Index} is a transport station (has TransportStationData)");
                return true;
            }
            
            // CargoTransportStationData indicates a cargo station
            if (EntityManager.HasComponent<Game.Prefabs.CargoTransportStationData>(prefab))
            {
                m_Log.Info($"Building {buildingEntity.Index} is a cargo transport station");
                return true;
            }
            
            // Also check for PublicTransportStationData (for some station types)
            if (EntityManager.HasComponent<Game.Prefabs.PublicTransportStationData>(prefab))
            {
                m_Log.Info($"Building {buildingEntity.Index} is a public transport station");
                return true;
            }
            
            // Check for TransportDepotData (bus depots, train yards, etc.)
            if (EntityManager.HasComponent<Game.Prefabs.TransportDepotData>(prefab))
            {
                m_Log.Info($"Building {buildingEntity.Index} is a transport depot");
                return true;
            }
            
            return false;
        }
    }
}
