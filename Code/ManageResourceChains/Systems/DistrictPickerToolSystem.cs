using Colossal.Logging;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Game.UI.InGame;
using ManageResourceChains;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// A tool for district picking that blocks DefaultToolSystem from processing clicks.
    /// Handles district detection and notification to ResourceChainManagementSystem.
    /// </summary>
    public partial class DistrictPickerToolSystem : ToolBaseSystem
    {
        private ILog m_Log;
        private EntityQuery m_HighlightedQuery;
        private ToolOutputBarrier m_Barrier;
        private NativeList<Entity> m_SelectedDistricts;
        private ResourceChainManagementSystem m_ResourceChainManagementSystem;
        private SelectedInfoUISystem m_SelectedInfoUISystem;

        /// <inheritdoc/>
        public override string toolID => "DistrictPickerTool";

        /// <summary>
        /// Gets the list of selected district entities.
        /// </summary>
        public NativeList<Entity> SelectedDistricts => m_SelectedDistricts;

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
            // Try both ground and areas - cast to whatever we can hit
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Areas; // Try both!
            m_ToolRaycastSystem.areaTypeMask = AreaTypeMask.Districts;
            
            m_Log.Info($"DistrictPickerTool raycast configured: TypeMask={m_ToolRaycastSystem.typeMask}, AreaTypeMask={m_ToolRaycastSystem.areaTypeMask}");
        }

        /// <inheritdoc/>
        public override bool allowUnderground => true;

        /// <inheritdoc/>
        protected override bool GetAllowApply()
        {
            // Don't consume apply action - let it pass through to normal district selection
            return false;
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
            m_SelectedDistricts.Clear();
            m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;
            m_Log = Mod.Log;
            m_Log.Info($"{nameof(DistrictPickerToolSystem)}.{nameof(OnCreate)}");
            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            m_SelectedDistricts = new NativeList<Entity>(Allocator.Persistent);
            
            m_HighlightedQuery = SystemAPI.QueryBuilder()
                .WithAll<Highlighted>()
                .WithNone<Deleted, Temp, Overridden>()
                .Build();
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (m_SelectedDistricts.IsCreated)
            {
                m_SelectedDistricts.Dispose();
            }
        }

        /// <inheritdoc/>
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_SelectedDistricts.Clear();
            applyAction.shouldBeEnabled = true;
            m_Log.Info($"✅ {nameof(DistrictPickerToolSystem)} STARTED RUNNING - tool active, blocking DefaultToolSystem");
        }

        /// <inheritdoc/>
        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            EntityManager.AddComponent<BatchesUpdated>(m_HighlightedQuery);
            EntityManager.RemoveComponent<Highlighted>(m_HighlightedQuery);
            m_Log.Info($"⛔ {nameof(DistrictPickerToolSystem)} STOPPED RUNNING");
        }

        /// <inheritdoc/>
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Log every update to see if tool is running
            if (UnityEngine.Time.frameCount % 60 == 0) // Every 60 frames (about once per second)
            {
                m_Log.Info($"🔄 DistrictPickerTool OnUpdate running (tool is active)");
            }
            
            // Check for any raycast results
            if (GetRaycastResult(out Entity hitEntity, out RaycastHit hit))
            {
                bool hasDistrict = EntityManager.HasComponent<District>(hitEntity);
                
                // Only log if it's a district or if we want to debug everything
                if (hasDistrict)
                {
                    m_Log.Info($"🎯 Raycast hit! Entity: {hitEntity.Index}, HasDistrict: {hasDistrict}");
                    
                    // Handle click to select district
                    if (applyAction.WasReleasedThisFrame())
                    {
                        m_Log.Info($"✅ District {hitEntity.Index} clicked in tool - adding to rule");
                        m_ResourceChainManagementSystem.OnDistrictSelected(hitEntity);
                    }
                }
            }
            
            // Check for Escape key to cancel
            if (cancelAction.WasPressedThisFrame())
            {
                m_Log.Info("⚠️ Escape pressed - cancelling district picker");
                m_ResourceChainManagementSystem.CancelDistrictPicker();
            }
            
            // This tool blocks DefaultToolSystem from processing clicks
            return inputDeps;
        }
    }
}

