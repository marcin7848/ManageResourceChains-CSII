using Colossal.Entities;
using Colossal.Logging;
using Game.Buildings;
using Game.Common;
using Game.Input;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using ManageResourceChains;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// A tool for picking multiple buildings to add to resource chain rules.
    /// </summary>
    public partial class BuildingPickerToolSystem : ToolBaseSystem
    {
        private ILog m_Log;
        private Entity m_PreviousRaycastedEntity;
        private EntityQuery m_HighlightedQuery;
        private ToolOutputBarrier m_Barrier;
        private NativeList<Entity> m_SelectedBuildings;
        private ResourceChainManagementSystem m_ResourceChainManagementSystem;

        /// <inheritdoc/>
        public override string toolID => "BuildingPickerTool";

        /// <summary>
        /// Gets the list of selected building entities.
        /// </summary>
        public NativeList<Entity> SelectedBuildings => m_SelectedBuildings;

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
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects;
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubBuildings;
        }

        /// <summary>
        /// Request to disable the tool and confirm selection.
        /// </summary>
        public void ConfirmSelection()
        {
            // Don't call ResourceChainManagementSystem here - it will call us back
            // Just switch to default tool
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        /// <summary>
        /// Request to disable the tool and cancel selection.
        /// </summary>
        public void CancelSelection()
        {
            m_SelectedBuildings.Clear();
            // Don't call ResourceChainManagementSystem here - it will call us back
            // Just switch to default tool
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_Log = Mod.Log;
            m_Log.Info($"{nameof(BuildingPickerToolSystem)}.{nameof(OnCreate)}");
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            m_SelectedBuildings = new NativeList<Entity>(Allocator.Persistent);
            
            m_HighlightedQuery = SystemAPI.QueryBuilder()
                .WithAll<Highlighted>()
                .WithNone<Deleted, Temp, Overridden>()
                .Build();
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (m_SelectedBuildings.IsCreated)
            {
                m_SelectedBuildings.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_SelectedBuildings.Clear();
            applyAction.shouldBeEnabled = true;
        }

        /// <inheritdoc/>
        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            EntityManager.AddComponent<BatchesUpdated>(m_HighlightedQuery);
            EntityManager.RemoveComponent<Highlighted>(m_HighlightedQuery);
            m_PreviousRaycastedEntity = Entity.Null;
            m_Log.Debug($"{nameof(BuildingPickerToolSystem)}.{nameof(OnStopRunning)}");
        }

        /// <inheritdoc/>
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            inputDeps = Dependency;
            EntityCommandBuffer buffer = m_Barrier.CreateCommandBuffer();

            // Check for Escape key (cancelAction) to cancel selection
            if (cancelAction.WasPressedThisFrame())
            {
                m_ResourceChainManagementSystem.CancelBuildingPicker();
                return inputDeps;
            }

            // Handle raycast and highlighting
            if (!GetRaycastResult(out Entity currentRaycastEntity, out RaycastHit hit))
            {
                buffer.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                buffer.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                m_PreviousRaycastedEntity = Entity.Null;
                return inputDeps;
            }

            // Only proceed if entity has Building component
            if (!EntityManager.HasComponent<Building>(currentRaycastEntity))
            {
                buffer.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                buffer.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                m_PreviousRaycastedEntity = Entity.Null;
                return inputDeps;
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

            // Check for mouse click to add building to selection
            if (applyAction.WasReleasedThisFrame())
            {
                // Check if not already selected
                bool alreadySelected = false;
                for (int i = 0; i < m_SelectedBuildings.Length; i++)
                {
                    if (m_SelectedBuildings[i] == currentRaycastEntity)
                    {
                        alreadySelected = true;
                        break;
                    }
                }

                if (!alreadySelected)
                {
                    m_SelectedBuildings.Add(currentRaycastEntity);
                    
                    // Immediately notify management system to update UI
                    m_ResourceChainManagementSystem.OnBuildingSelected(currentRaycastEntity);
                }
            }

            return inputDeps;
        }
    }
}

