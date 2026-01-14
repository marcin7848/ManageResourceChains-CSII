﻿using Colossal.Entities;
using Colossal.Logging;
using Game.Buildings;
using Game.Common;
using Game.Input;
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
            // Allow picking both buildings (stations) and waypoints (stops)
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.RouteWaypoints | TypeMask.Net;
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubBuildings | RaycastFlags.Markers | RaycastFlags.NoMainElements;
            
            // Set route type to TransportLine to detect transport stops
            m_ToolRaycastSystem.routeType = Game.Routes.RouteType.TransportLine;
            
            m_Log.Info($"InitializeRaycast called: typeMask={m_ToolRaycastSystem.typeMask}, routeType={m_ToolRaycastSystem.routeType}, collisionMask={m_ToolRaycastSystem.collisionMask}");
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

            // Log what we're hitting
            if (currentRaycastEntity != m_PreviousRaycastedEntity)
            {
                m_Log.Info($"Raycast hit entity {currentRaycastEntity.Index} (v{currentRaycastEntity.Version})");
                
                // Check what components this entity has
                bool hasBuilding = EntityManager.HasComponent<Building>(currentRaycastEntity);
                bool hasWaypoint = EntityManager.HasComponent<Waypoint>(currentRaycastEntity);
                bool hasTransportStop = EntityManager.HasComponent<Game.Routes.TransportStop>(currentRaycastEntity);
                bool hasConnected = EntityManager.HasComponent<Game.Routes.Connected>(currentRaycastEntity);
                bool hasOwner = EntityManager.HasComponent<Owner>(currentRaycastEntity);
                bool hasPrefabRef = EntityManager.HasComponent<PrefabRef>(currentRaycastEntity);
                
                m_Log.Info($"  Components: Building={hasBuilding}, Waypoint={hasWaypoint}, TransportStop={hasTransportStop}, Connected={hasConnected}, Owner={hasOwner}, PrefabRef={hasPrefabRef}");
                
                // If it has PrefabRef, log the prefab info
                if (hasPrefabRef)
                {
                    var prefabRef = EntityManager.GetComponentData<PrefabRef>(currentRaycastEntity);
                    m_Log.Info($"  Prefab entity: {prefabRef.m_Prefab.Index}");
                }
            }

            // Check if it's a valid transport priority target
            // It should be either a Building or a Waypoint with TransportStop
            bool isValid = EntityManager.HasComponent<Building>(currentRaycastEntity) || 
                           (EntityManager.HasComponent<Waypoint>(currentRaycastEntity) && EntityManager.HasComponent<Game.Routes.TransportStop>(currentRaycastEntity));

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

            // Update highlighting
            if (currentRaycastEntity != m_PreviousRaycastedEntity)
            {
                m_PreviousRaycastedEntity = currentRaycastEntity;
                NativeArray<Entity> entities = m_HighlightedQuery.ToEntityArray(Allocator.Temp);
                buffer.AddComponent<BatchesUpdated>(entities);
                buffer.RemoveComponent<Highlighted>(entities);
            }

            if (m_HighlightedQuery.IsEmptyIgnoreFilter)
            {
                buffer.AddComponent<BatchesUpdated>(currentRaycastEntity);
                buffer.AddComponent<Highlighted>(currentRaycastEntity);
                m_PreviousRaycastedEntity = currentRaycastEntity;
            }

            // Check for mouse click
            if (applyAction.WasReleasedThisFrame())
            {
                m_Log.Info($"Apply action pressed! Selecting entity {currentRaycastEntity.Index}");
                m_ResourceChainManagementSystem.OnPrioritySelected(currentRaycastEntity);
            }

            return inputDeps;
        }
    }
}
