using Colossal.UI.Binding;
using Game.Buildings;
using Game.UI;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// Simple UI System that tracks building selection and provides data to the UI
    /// </summary>
    public partial class BuildingSelectionUISystem : UISystemBase
    {
        private ValueBinding<bool> _isBuildingSelected;
        private ValueBinding<int> _selectedBuildingEntity;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            
            Mod.log.Info("===== BuildingSelectionUISystem created =====");
            
            // Create bindings that the UI can subscribe to
            AddBinding(_isBuildingSelected = new ValueBinding<bool>("manageResourceChains", "isBuildingSelected", false));
            AddBinding(_selectedBuildingEntity = new ValueBinding<int>("manageResourceChains", "selectedBuildingEntity", 0));
            
            Mod.log.Info("Bindings created successfully");
        }

        protected override void OnUpdate()
        {
            // Get the selected entity from the game's default tool system
            var toolSystem = World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>();
            var selectedEntity = toolSystem.selected;
            
            // Check if a building is selected
            bool isBuildingSelected = selectedEntity != Entity.Null && 
                                      EntityManager.HasComponent<Building>(selectedEntity);
            
            // ValueBinding.Update() is smart - it only triggers UI updates when the value actually changes
            // So calling it every frame is fine and expected in ECS
            _isBuildingSelected.Update(isBuildingSelected);
            _selectedBuildingEntity.Update(isBuildingSelected ? selectedEntity.Index : 0);
        }
    }
}
