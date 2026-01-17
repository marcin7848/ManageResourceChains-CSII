using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.UI.Binding;
using Game.UI;
using Game.Tools;
using Unity.Entities;
using Newtonsoft.Json;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// Unified system that manages resource chain configurations for both buildings and districts
    /// </summary>
    public partial class ResourceChainManagementSystem : UISystemBase
    {
        // Active configurations used by game logic (only updated when "Save all changes" is clicked)
        // Key format: "entityId_type" (e.g., "12345_Building" or "67890_District")
        private static Dictionary<string, Data.BuildingConfiguration> _activeConfigurations = new Dictionary<string, Data.BuildingConfiguration>();
        
        // Staging configurations for UI editing (not used by game logic until committed)
        private Dictionary<string, Data.BuildingConfiguration> _stagingConfigurations = new Dictionary<string, Data.BuildingConfiguration>();
        
        // Bindings
        private ValueBinding<string> _resourceChainConfigBinding;
        private ValueBinding<string> _districtConfigBinding;
        private ValueBinding<bool> _buildingPickerActiveBinding;
        private ValueBinding<bool> _districtPickerActiveBinding;
        private ValueBinding<bool> _priorityPickerActiveBinding;
        private ValueBinding<string> _allDistrictsBinding;
        
        // Tool systems
        private BuildingPickerToolSystem _buildingPickerToolSystem;
        private DistrictPickerToolSystem _districtPickerToolSystem;
        private PriorityPickerToolSystem _priorityPickerToolSystem;
        private ToolSystem _toolSystem;
        
        // State for tracking picker
        private string _currentRuleId;
        private int _currentEntityId;
        private Data.EntityType _currentEntityType;
        private bool _isPickingDistricts; 
        private bool _isPickingPriorities;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("ResourceChainManagementSystem created");
            
            // Get tool systems
            _buildingPickerToolSystem = World.GetOrCreateSystemManaged<BuildingPickerToolSystem>();
            _districtPickerToolSystem = World.GetOrCreateSystemManaged<DistrictPickerToolSystem>();
            _priorityPickerToolSystem = World.GetOrCreateSystemManaged<PriorityPickerToolSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            
            // Add binding to send configuration data to UI
            AddBinding(_resourceChainConfigBinding = new ValueBinding<string>("manageResourceChains", "resourceChainConfig", "{}"));
            AddBinding(_districtConfigBinding = new ValueBinding<string>("manageResourceChains", "districtConfig", "{}"));
            AddBinding(_buildingPickerActiveBinding = new ValueBinding<bool>("manageResourceChains", "buildingPickerActive", false));
            AddBinding(_districtPickerActiveBinding = new ValueBinding<bool>("manageResourceChains", "districtPickerActive", false));
            AddBinding(_priorityPickerActiveBinding = new ValueBinding<bool>("manageResourceChains", "priorityPickerActive", false));
            
            // Add method bindings for UI to call
            AddBinding(new TriggerBinding<int>("manageResourceChains", "requestBuildingConfig", RequestBuildingConfig));
            AddBinding(new TriggerBinding<int>("manageResourceChains", "requestDistrictConfig", RequestDistrictConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "saveBuildingConfig", SaveBuildingConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "saveDistrictConfig", SaveDistrictConfig));
            AddBinding(new TriggerBinding("manageResourceChains", "saveAllConfigurations", SaveAllConfigurationsToDisk));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "addResourceChainRule", AddResourceChainRule));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "removeResourceChainRule", RemoveResourceChainRule));
            AddBinding(new TriggerBinding<int, string, string>("manageResourceChains", "updateResourceChainRule", UpdateResourceChainRule));
            
            // Building picker tool bindings
            AddBinding(new TriggerBinding<int, string, bool>("manageResourceChains", "startBuildingPicker", StartBuildingPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "confirmBuildingPicker", ConfirmBuildingPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "cancelBuildingPicker", CancelBuildingPicker));
            
            // District picker tool bindings
            AddBinding(new TriggerBinding<int, string, bool>("manageResourceChains", "startDistrictPicker", StartDistrictPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "confirmDistrictPicker", ConfirmDistrictPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "cancelDistrictPicker", CancelDistrictPicker));
            
            // Priority picker tool bindings
            AddBinding(new TriggerBinding<int, string, bool>("manageResourceChains", "startPriorityPicker", StartPriorityPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "confirmPriorityPicker", ConfirmPriorityPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "cancelPriorityPicker", CancelPriorityPicker));
            
            // Direct district management (no picker needed)
            AddBinding(new TriggerBinding<int, string, int>("manageResourceChains", "addDistrictToRule", AddDistrictToRule));
            AddBinding(new TriggerBinding("manageResourceChains", "requestAllDistricts", RequestAllDistricts));
            AddBinding(_allDistrictsBinding = new ValueBinding<string>("manageResourceChains", "allDistricts", "[]"));
            
            // Load configurations from file
            LoadConfigurations();
        }

        protected override void OnUpdate()
        {
            // Nothing to update each frame
        }

        #region Key Generation

        /// <summary>
        /// Generate a unique key for an entity configuration
        /// </summary>
        public static string GetConfigKey(int entityId, Data.EntityType type)
        {
            return $"{entityId}_{type}";
        }

        #endregion

        #region Request Configuration

        /// <summary>
        /// Request configuration for a specific entity (building or district)
        /// </summary>
        private void RequestEntityConfig(int entityId, Data.EntityType entityType)
        {
            try
            {
                string entityTypeName = entityType == Data.EntityType.Building ? "building" : "district";
                Mod.log.Info($"Requesting config for {entityTypeName} {entityId}");
                
                string key = GetConfigKey(entityId, entityType);
                var configBinding = entityType == Data.EntityType.Building ? _resourceChainConfigBinding : _districtConfigBinding;
                
                // Initialize staging config from active config if not already present
                if (!_stagingConfigurations.ContainsKey(key))
                {
                    if (_activeConfigurations.ContainsKey(key))
                    {
                        // Deep copy from active config
                        var activeConfig = _activeConfigurations[key];
                        _stagingConfigurations[key] = DeepCopyConfiguration(activeConfig);
                    }
                    else
                    {
                        // Create new empty config
                        _stagingConfigurations[key] = new Data.BuildingConfiguration
                        {
                            BuildingEntityId = entityId,
                            Type = entityType,
                            Rules = new List<Data.ResourceChainRule>()
                        };
                    }
                }
                
                var config = _stagingConfigurations[key];
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                Mod.log.Info($"Sending {entityTypeName} config: {json}");
                configBinding.Update(json);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error requesting entity config: {ex.Message}");
                var configBinding = entityType == Data.EntityType.Building ? _resourceChainConfigBinding : _districtConfigBinding;
                configBinding.Update("{}");
            }
        }

        /// <summary>
        /// Request configuration for a specific building (wrapper for backward compatibility)
        /// </summary>
        private void RequestBuildingConfig(int buildingEntityId)
        {
            RequestEntityConfig(buildingEntityId, Data.EntityType.Building);
        }

        /// <summary>
        /// Request configuration for a specific district (wrapper for backward compatibility)
        /// </summary>
        private void RequestDistrictConfig(int districtEntityId)
        {
            RequestEntityConfig(districtEntityId, Data.EntityType.District);
        }

        #endregion

        #region Save Configuration

        /// <summary>
        /// Save configuration for an entity (to staging only, not applied to game logic)
        /// </summary>
        private void SaveEntityConfig(int entityId, string configJson, Data.EntityType entityType)
        {
            try
            {
                string entityTypeName = entityType == Data.EntityType.Building ? "building" : "district";
                Mod.log.Info($"Saving config for {entityTypeName} {entityId} to staging");
                
                var config = JsonConvert.DeserializeObject<Data.BuildingConfiguration>(configJson);
                if (config != null)
                {
                    string key = GetConfigKey(entityId, entityType);
                    config.Type = entityType; // Ensure type is set correctly
                    config.BuildingEntityId = entityId; // Ensure ID is set correctly
                    _stagingConfigurations[key] = config;
                    Mod.log.Info($"✓ Saved {entityTypeName} config to staging (not applied to game logic yet)");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving entity config: {ex.Message}");
            }
        }

        /// <summary>
        /// Save configuration for a building (wrapper for backward compatibility)
        /// </summary>
        private void SaveBuildingConfig(int buildingEntityId, string configJson)
        {
            SaveEntityConfig(buildingEntityId, configJson, Data.EntityType.Building);
        }

        /// <summary>
        /// Save configuration for a district (wrapper for backward compatibility)
        /// </summary>
        private void SaveDistrictConfig(int districtEntityId, string configJson)
        {
            SaveEntityConfig(districtEntityId, configJson, Data.EntityType.District);
        }

        #endregion

        #region Rule Management

        /// <summary>
        /// Add a new resource chain rule (to staging only)
        /// </summary>
        private void AddResourceChainRule(int entityId, string ruleJson)
        {
            try
            {
                Mod.log.Info($"Adding rule for entity {entityId} to staging");
                
                // For backward compatibility, assume building if no type specified
                // UI should eventually pass entity type
                string key = GetConfigKey(entityId, Data.EntityType.Building);
                
                if (!_stagingConfigurations.ContainsKey(key))
                {
                    _stagingConfigurations[key] = new Data.BuildingConfiguration
                    {
                        BuildingEntityId = entityId,
                        Type = Data.EntityType.Building,
                        Rules = new List<Data.ResourceChainRule>()
                    };
                }
                
                var rule = JsonConvert.DeserializeObject<Data.ResourceChainRule>(ruleJson) ?? new Data.ResourceChainRule();
                _stagingConfigurations[key].Rules.Add(rule);
                
                Mod.log.Info($"✓ Added rule to staging (not applied to game logic yet)");
                RequestBuildingConfig(entityId);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error adding rule: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove a resource chain rule (from staging only)
        /// </summary>
        private void RemoveResourceChainRule(int entityId, string ruleId)
        {
            try
            {
                Mod.log.Info($"Removing rule {ruleId} for entity {entityId} from staging");
                
                // For backward compatibility, assume building
                string key = GetConfigKey(entityId, Data.EntityType.Building);
                
                if (_stagingConfigurations.ContainsKey(key))
                {
                    var config = _stagingConfigurations[key];
                    config.Rules.RemoveAll(r => r.Id == ruleId);
                    
                    Mod.log.Info($"✓ Removed rule from staging (not applied to game logic yet)");
                    RequestBuildingConfig(entityId);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error removing rule: {ex.Message}");
            }
        }

        /// <summary>
        /// Update an existing resource chain rule (in staging only)
        /// </summary>
        private void UpdateResourceChainRule(int entityId, string ruleId, string ruleJson)
        {
            try
            {
                Mod.log.Info($"Updating rule {ruleId} for entity {entityId} in staging");
                
                // For backward compatibility, assume building
                string key = GetConfigKey(entityId, Data.EntityType.Building);
                
                if (_stagingConfigurations.ContainsKey(key))
                {
                    var config = _stagingConfigurations[key];
                    var existingRuleIndex = config.Rules.FindIndex(r => r.Id == ruleId);
                    
                    if (existingRuleIndex >= 0)
                    {
                        var updatedRule = JsonConvert.DeserializeObject<Data.ResourceChainRule>(ruleJson);
                        if (updatedRule != null)
                        {
                            config.Rules[existingRuleIndex] = updatedRule;
                            Mod.log.Info($"✓ Updated rule in staging (not applied to game logic yet)");
                            RequestBuildingConfig(entityId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error updating rule: {ex.Message}");
            }
        }

        #endregion

        #region Load/Save to Disk

        /// <summary>
        /// Load configurations from a single unified file
        /// </summary>
        private void LoadConfigurations()
        {
            try
            {
                string configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var configs = JsonConvert.DeserializeObject<List<Data.BuildingConfiguration>>(json);
                    
                    if (configs != null)
                    {
                        _activeConfigurations.Clear();
                        foreach (var config in configs)
                        {
                            string key = GetConfigKey(config.BuildingEntityId, config.Type);
                            _activeConfigurations[key] = config;
                        }
                        
                        Mod.log.Info($"Loaded {configs.Count} entity configurations from unified file");
                    }
                }
                else
                {
                    Mod.log.Info("No configuration file found - starting with empty configurations");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error loading configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Save all configurations to disk (called from UI when "Save all changes" is clicked)
        /// This commits staging changes to active configurations and saves to a single unified file
        /// </summary>
        private void SaveAllConfigurationsToDisk()
        {
            try
            {
                Mod.log.Info("Committing staging changes to active configurations and saving to disk...");
                
                // Commit all staging changes to active configurations
                foreach (var kvp in _stagingConfigurations)
                {
                    _activeConfigurations[kvp.Key] = DeepCopyConfiguration(kvp.Value);
                    var config = kvp.Value;
                    string entityTypeName = config.Type == Data.EntityType.Building ? "building" : "district";
                    Mod.log.Info($"Committed {entityTypeName} config for entity {config.BuildingEntityId}");
                }
                
                // Save to disk
                SaveConfigurations();
                
                Mod.log.Info("All configurations committed and saved successfully - game logic will now use updated rules");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving all configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Save configurations to a single unified file
        /// </summary>
        private void SaveConfigurations()
        {
            try
            {
                string configPath = GetConfigFilePath();
                var allConfigs = _activeConfigurations.Values.ToList();
                string json = JsonConvert.SerializeObject(allConfigs, Formatting.Indented);
                
                string directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(configPath, json);
                
                Mod.log.Info($"Saved {allConfigs.Count} entity configurations to unified file");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the unified configuration file path
        /// </summary>
        private string GetConfigFilePath()
        {
            string userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string modPath = Path.Combine(userDataPath, "Colossal Order", "Cities Skylines II", "ModsData", "ManageResourceChains");
            return Path.Combine(modPath, "entity_chain_configs.json");
        }


        #endregion

        #region Helper Methods

        /// <summary>
        /// Create a deep copy of a configuration
        /// </summary>
        private Data.BuildingConfiguration DeepCopyConfiguration(Data.BuildingConfiguration original)
        {
            string json = JsonConvert.SerializeObject(original);
            return JsonConvert.DeserializeObject<Data.BuildingConfiguration>(json) ?? new Data.BuildingConfiguration
            {
                BuildingEntityId = original.BuildingEntityId,
                Type = original.Type,
                Rules = new List<Data.ResourceChainRule>()
            };
        }

        #endregion

        #region Building Picker Tool

        /// <summary>
        /// Start building picker tool
        /// </summary>
        private void StartBuildingPicker(int entityId, string ruleId, bool isDistrict)
        {
            try
            {
                _currentEntityType = isDistrict ? Data.EntityType.District : Data.EntityType.Building;
                string entityTypeName = isDistrict ? "district" : "building";
                Mod.log.Info($"Starting building picker for {entityTypeName} {entityId}, rule {ruleId}");
                
                _currentEntityId = entityId;
                _currentRuleId = ruleId;
                
                // Activate the building picker tool
                _toolSystem.activeTool = _buildingPickerToolSystem;
                _buildingPickerActiveBinding.Update(true);
                
                Mod.log.Info("Building picker tool activated");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error starting building picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a building is selected during picking mode - updates UI immediately
        /// </summary>
        public void OnBuildingSelected(Entity buildingEntity)
        {
            try
            {
                int buildingId = buildingEntity.Index;
                string entityTypeName = _currentEntityType == Data.EntityType.Building ? "building" : "district";
                Mod.log.Info($"Building {buildingId} selected for {entityTypeName} {_currentEntityId}, updating staging");
                
                string key = GetConfigKey(_currentEntityId, _currentEntityType);
                var configBinding = _currentEntityType == Data.EntityType.Building ? _resourceChainConfigBinding : _districtConfigBinding;
                
                // Ensure config exists for this entity in staging
                if (!_stagingConfigurations.TryGetValue(key, out var config))
                {
                    Mod.log.Warn($"Staging config not found for {entityTypeName} {_currentEntityId}, this should not happen");
                    return;
                }
                
                // Find the rule
                var rule = config.Rules.FirstOrDefault(r => r.Id == _currentRuleId);
                if (rule == null)
                {
                    Mod.log.Warn($"Rule {_currentRuleId} not found in staging config for {entityTypeName} {_currentEntityId}");
                    return;
                }
                
                // Add building to the rule if not already present
                if (!rule.Buildings.Contains(buildingId))
                {
                    rule.Buildings.Add(buildingId);
                    Mod.log.Info($"✓ Added building {buildingId} to {entityTypeName} rule {_currentRuleId} in staging");
                    
                    // Send updated config to UI immediately
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    configBinding.Update(json);
                    
                    Mod.log.Info($"UI updated with new building for {entityTypeName} (staging only, not applied to game logic)");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error in OnBuildingSelected: {ex.Message}");
            }
        }

        /// <summary>
        /// Confirm building picker selection
        /// </summary>
        public void ConfirmBuildingPicker()
        {
            try
            {
                Mod.log.Info("Confirming building picker selection");
                
                _buildingPickerToolSystem.ConfirmSelection();
                _buildingPickerActiveBinding.Update(false);
                
                Mod.log.Info("Building picker confirmed (changes in staging, user must click 'Save All Changes')");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error confirming building picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel building picker selection
        /// </summary>
        public void CancelBuildingPicker()
        {
            try
            {
                Mod.log.Info("Cancelling building picker");
                
                _buildingPickerToolSystem.CancelSelection();
                _buildingPickerActiveBinding.Update(false);
                
                Mod.log.Info("Building picker cancelled");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error cancelling building picker: {ex.Message}");
            }
        }

        #endregion

        #region District Picker Tool

        /// <summary>
        /// <summary>
        /// Check if district picker is currently active
        /// </summary>
        public bool IsDistrictPickerActive()
        {
            return _isPickingDistricts;
        }

        /// <summary>
        /// Check if priority picker is currently active
        /// </summary>
        public bool IsPriorityPickerActive()
        {
            return _isPickingPriorities;
        }

        /// <summary>
        /// Start district picker - Activate the tool to block context switching
        /// </summary>
        private void StartDistrictPicker(int entityId, string ruleId, bool isDistrict)
        {
            try
            {
                _currentEntityType = isDistrict ? Data.EntityType.District : Data.EntityType.Building;
                _currentEntityId = entityId;
                _currentRuleId = ruleId;
                _isPickingDistricts = true;
                
                // Activate the tool - this blocks DefaultToolSystem and prevents context switching
                _toolSystem.activeTool = _districtPickerToolSystem;
                _districtPickerActiveBinding.Update(true);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error starting district picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a district is selected during picking mode - updates UI immediately
        /// </summary>
        public void OnDistrictSelected(Entity districtEntity)
        {
            try
            {
                int districtId = districtEntity.Index;
                string key = GetConfigKey(_currentEntityId, _currentEntityType);
                var configBinding = _currentEntityType == Data.EntityType.Building ? _resourceChainConfigBinding : _districtConfigBinding;
                
                // Ensure config exists for this entity in staging
                if (!_stagingConfigurations.TryGetValue(key, out var config))
                {
                    return;
                }
                
                // Find the rule
                var rule = config.Rules.FirstOrDefault(r => r.Id == _currentRuleId);
                if (rule == null)
                {
                    return;
                }
                
                // Add district to the rule if not already present
                if (!rule.Districts.Contains(districtId))
                {
                    rule.Districts.Add(districtId);
                    
                    // Send updated config to UI immediately
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    configBinding.Update(json);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error in OnDistrictSelected: {ex.Message}");
            }
        }

        /// <summary>
        /// Confirm district picker selection
        /// </summary>
        public void ConfirmDistrictPicker()
        {
            try
            {
                _districtPickerToolSystem.ConfirmSelection();
                _isPickingDistricts = false;
                _districtPickerActiveBinding.Update(false);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error confirming district picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel district picker selection
        /// </summary>
        public void CancelDistrictPicker()
        {
            try
            {
                _districtPickerToolSystem.CancelSelection();
                _isPickingDistricts = false;
                _districtPickerActiveBinding.Update(false);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error cancelling district picker: {ex.Message}");
            }
        }

        #endregion

        #region Priority Picker Tool

        /// <summary>
        /// Start priority picker tool
        /// </summary>
        private void StartPriorityPicker(int entityId, string ruleId, bool isDistrict)
        {
            try
            {
                _currentEntityType = isDistrict ? Data.EntityType.District : Data.EntityType.Building;
                Mod.log.Info($"Starting priority picker for {entityId}, rule {ruleId}");
                
                _currentEntityId = entityId;
                _currentRuleId = ruleId;
                _isPickingPriorities = true;
                
                // Activate the priority picker tool
                _toolSystem.activeTool = _priorityPickerToolSystem;
                _priorityPickerActiveBinding.Update(true);
                
                Mod.log.Info("Priority picker tool activated");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error starting priority picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a transport entity is selected during picking mode
        /// </summary>
        public void OnPrioritySelected(Entity priorityEntity)
        {
            try
            {
                int entityId = priorityEntity.Index;
                string key = GetConfigKey(_currentEntityId, _currentEntityType);
                var configBinding = _currentEntityType == Data.EntityType.Building ? _resourceChainConfigBinding : _districtConfigBinding;
                
                // Ensure config exists for this entity in staging
                if (!_stagingConfigurations.TryGetValue(key, out var config))
                {
                    return;
                }
                
                // Find the rule
                var rule = config.Rules.FirstOrDefault(r => r.Id == _currentRuleId);
                if (rule == null)
                {
                    return;
                }
                
                // Determine station type based on ECS components
                Data.TransportStationType stationType = DetermineStationType(priorityEntity);
                
                // Add priority to the rule
                var priority = new Data.TransportPriority
                {
                    Id = Guid.NewGuid().ToString(),
                    StationEntity = entityId,
                    StationType = stationType,
                    Priority = 10 // Default priority
                };
                
                rule.TransportPriorities.Add(priority);
                Mod.log.Info($"✓ Added priority {stationType} (entity {entityId}) to rule {_currentRuleId} in staging");
                
                // Send updated config to UI
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                configBinding.Update(json);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error in OnPrioritySelected: {ex.Message}");
            }
        }

        /// <summary>
        /// Confirm priority picker selection
        /// </summary>
        public void ConfirmPriorityPicker()
        {
            try
            {
                _priorityPickerToolSystem.ConfirmSelection();
                _isPickingPriorities = false;
                _priorityPickerActiveBinding.Update(false);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error confirming priority picker: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel priority picker selection
        /// </summary>
        public void CancelPriorityPicker()
        {
            try
            {
                _priorityPickerToolSystem.CancelSelection();
                _isPickingPriorities = false;
                _priorityPickerActiveBinding.Update(false);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error cancelling priority picker: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Determine the transport station type from an entity's components
        /// </summary>
        private Data.TransportStationType DetermineStationType(Entity entity)
        {
            try
            {
                // Check for specific stop component types (for waypoints/stops)
                if (EntityManager.HasComponent<Game.Routes.TaxiStand>(entity))
                    return Data.TransportStationType.TaxiStand;
                    
                if (EntityManager.HasComponent<Game.Routes.BusStop>(entity))
                    return Data.TransportStationType.BusStop;
                    
                if (EntityManager.HasComponent<Game.Routes.TramStop>(entity))
                    return Data.TransportStationType.TramStop;
                    
                if (EntityManager.HasComponent<Game.Routes.TrainStop>(entity))
                    return Data.TransportStationType.TrainStation;
                    
                if (EntityManager.HasComponent<Game.Routes.SubwayStop>(entity))
                    return Data.TransportStationType.SubwayStation;
                    
                if (EntityManager.HasComponent<Game.Routes.FerryStop>(entity))
                    return Data.TransportStationType.FerryTerminal;
                    
                if (EntityManager.HasComponent<Game.Routes.ShipStop>(entity))
                    return Data.TransportStationType.Port;
                    
                if (EntityManager.HasComponent<Game.Routes.AirplaneStop>(entity))
                    return Data.TransportStationType.Airport;
                
                // Check for building station types (for buildings)
                if (EntityManager.HasComponent<Game.Buildings.Building>(entity))
                {
                    // Check if it has PrefabRef to inspect the prefab
                    if (EntityManager.HasComponent<Game.Prefabs.PrefabRef>(entity))
                    {
                        var prefabRef = EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(entity);
                        var prefabEntity = prefabRef.m_Prefab;
                        
                        // Check for cargo transport station
                        if (EntityManager.HasComponent<Game.Prefabs.CargoTransportStationData>(prefabEntity))
                            return Data.TransportStationType.CargoTerminal;
                        
                        // Check for public transport station (could be train, bus, etc.)
                        if (EntityManager.HasComponent<Game.Prefabs.PublicTransportStationData>(prefabEntity))
                        {
                            // Try to determine specific type from the station name or other characteristics
                            // For now, default to TrainStation for buildings with PublicTransportStationData
                            return Data.TransportStationType.TrainStation;
                        }
                    }
                    
                    // Default for buildings
                    return Data.TransportStationType.BusStation;
                }
                
                // Default fallback
                return Data.TransportStationType.BusStop;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error determining station type: {ex.Message}");
                return Data.TransportStationType.BusStop;
            }
        }

        #endregion

        #region Public API for Game Systems

        /// <summary>
        /// Get all active configurations (both buildings and districts) - for use by other systems
        /// </summary>
        public static Dictionary<string, Data.BuildingConfiguration> GetActiveConfigurations()
        {
            return _activeConfigurations;
        }

        /// <summary>
        /// Get all building configurations (for use by other systems like pathfinding)
        /// </summary>
        public Dictionary<int, Data.BuildingConfiguration> GetAllConfigurations()
        {
            // Return only building configs for backward compatibility
            return _activeConfigurations
                .Where(kvp => kvp.Value.Type == Data.EntityType.Building)
                .ToDictionary(kvp => kvp.Value.BuildingEntityId, kvp => kvp.Value);
        }

        /// <summary>
        /// Get all district configurations (for use by other systems like pathfinding)
        /// </summary>
        public Dictionary<int, Data.BuildingConfiguration> GetAllDistrictConfigurations()
        {
            // Return only district configs for backward compatibility
            return _activeConfigurations
                .Where(kvp => kvp.Value.Type == Data.EntityType.District)
                .ToDictionary(kvp => kvp.Value.BuildingEntityId, kvp => kvp.Value);
        }

        /// <summary>
        /// Get configuration for a specific building
        /// </summary>
        public static Data.BuildingConfiguration GetBuildingConfiguration(int buildingEntityId)
        {
            string key = GetConfigKey(buildingEntityId, Data.EntityType.Building);
            if (_activeConfigurations.ContainsKey(key))
            {
                return _activeConfigurations[key];
            }
            
            return new Data.BuildingConfiguration
            {
                BuildingEntityId = buildingEntityId,
                Type = Data.EntityType.Building,
                Rules = new List<Data.ResourceChainRule>()
            };
        }

        #endregion

        #region Direct District Management

        /// <summary>
        /// Request all districts in the game to populate a dropdown list
        /// </summary>
        private void RequestAllDistricts()
        {
            try
            {
                Mod.log.Info("Requesting all districts");
                
                // Query all districts
                var districtQuery = SystemAPI.QueryBuilder()
                    .WithAll<Game.Areas.District>()
                    .WithNone<Game.Common.Deleted, Game.Tools.Temp>()
                    .Build();
                
                var districts = districtQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                
                // Create a list of district info
                var districtList = new List<object>();
                foreach (var district in districts)
                {
                    districtList.Add(new { id = district.Index, name = $"District {district.Index}" });
                }
                
                districts.Dispose();
                
                // Serialize and send to UI
                string json = JsonConvert.SerializeObject(districtList);
                _allDistrictsBinding.Update(json);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error requesting all districts: {ex.Message}");
                _allDistrictsBinding.Update("[]");
            }
        }

        /// <summary>
        /// Add a district to a rule directly by ID (no picker needed)
        /// </summary>
        private void AddDistrictToRule(int entityId, string ruleId, int districtId)
        {
            try
            {
                // For now, assume building entity type - UI should pass this
                string key = GetConfigKey(entityId, Data.EntityType.Building);
                
                // Ensure config exists in staging
                if (!_stagingConfigurations.TryGetValue(key, out var config))
                {
                    return;
                }
                
                // Find the rule
                var rule = config.Rules.FirstOrDefault(r => r.Id == ruleId);
                if (rule == null)
                {
                    return;
                }
                
                // Add district if not already present
                if (!rule.Districts.Contains(districtId))
                {
                    rule.Districts.Add(districtId);
                    
                    // Update UI
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    _resourceChainConfigBinding.Update(json);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error adding district to rule: {ex.Message}");
            }
        }

        #endregion
    }
}

