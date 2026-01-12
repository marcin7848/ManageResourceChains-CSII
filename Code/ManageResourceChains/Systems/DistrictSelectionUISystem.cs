using Colossal.Logging;
using Colossal.UI.Binding;
using Game.Areas;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that provides a "Manage Resource Chains" button in the district info panel.
    /// Similar to BuildingSelectionUISystem but for districts.
    /// </summary>
    public partial class DistrictSelectionUISystem : UISystemBase
    {
        private ILog m_Log;
        private Entity m_SelectedDistrict;
        private SelectedInfoUISystem m_SelectedInfoUISystem;
        
        private ValueBinding<bool> m_IsDistrictSelectedBinding;
        private ValueBinding<int> m_SelectedDistrictEntityBinding;
        private ValueBinding<string> m_DistrictConfigBinding;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Log = Mod.Log;
            m_Log.Info($"{nameof(DistrictSelectionUISystem)} created");
            m_SelectedDistrict = Entity.Null;
            
            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            
            // Create UI bindings for district selection
            AddBinding(m_IsDistrictSelectedBinding = new ValueBinding<bool>("manageResourceChains", "isDistrictSelected", false));
            AddBinding(m_SelectedDistrictEntityBinding = new ValueBinding<int>("manageResourceChains", "selectedDistrictEntity", 0));
            AddBinding(m_DistrictConfigBinding = new ValueBinding<string>("manageResourceChains", "districtConfig", "{}"));
            
            m_Log.Info($"{nameof(DistrictSelectionUISystem)} bindings created");
        }

        protected override void OnUpdate()
        {
            // Check if a district is currently selected
            Entity selectedEntity = m_SelectedInfoUISystem.selectedEntity;
            bool hasDistrict = selectedEntity != Entity.Null && EntityManager.HasComponent<District>(selectedEntity);
            
            m_IsDistrictSelectedBinding.Update(hasDistrict);
            
            if (hasDistrict)
            {
                if (m_SelectedDistrict != selectedEntity)
                {
                    m_SelectedDistrict = selectedEntity;
                    m_SelectedDistrictEntityBinding.Update(selectedEntity.Index);
                    m_Log.Info($"🏘️ District selected: {selectedEntity.Index} (Version: {selectedEntity.Version})");
                    
                    // TODO: Load district config from storage
                    // For now, return empty config
                    m_DistrictConfigBinding.Update("{}");
                }
            }
            else
            {
                if (m_SelectedDistrict != Entity.Null)
                {
                    m_Log.Info($"🏘️ District deselected");
                    m_SelectedDistrict = Entity.Null;
                    m_SelectedDistrictEntityBinding.Update(0);
                    m_DistrictConfigBinding.Update("{}");
                }
            }
        }

        /// <summary>
        /// Called from UI when a district is selected.
        /// </summary>
        public void SetSelectedDistrict(Entity district)
        {
            m_SelectedDistrict = district;
            m_Log.Info($"Selected district: {district.Index} (Version: {district.Version})");
        }

        /// <summary>
        /// Gets the currently selected district.
        /// </summary>
        public Entity GetSelectedDistrict()
        {
            return m_SelectedDistrict;
        }

        /// <summary>
        /// Checks if the given entity is a valid district.
        /// </summary>
        public bool IsValidDistrict(Entity entity)
        {
            if (entity == Entity.Null)
                return false;

            // Check if entity has District component
            bool hasDistrict = EntityManager.HasComponent<District>(entity);
            
            m_Log.Info($"IsValidDistrict({entity.Index}): {hasDistrict}");
            
            return hasDistrict;
        }
    }
}

