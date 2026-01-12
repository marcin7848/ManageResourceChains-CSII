﻿using System;
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
    /// System that manages resource chain configurations and provides data to UI
    /// </summary>
    public partial class ResourceChainManagementSystem : UISystemBase
    {
        // Storage for all building configurations (in-memory)
        private static Dictionary<int, Data.BuildingConfiguration> _buildingConfigurations = new Dictionary<int, Data.BuildingConfiguration>();
        
        // Storage for all district configurations (in-memory)
        private static Dictionary<int, Data.BuildingConfiguration> _districtConfigurations = new Dictionary<int, Data.BuildingConfiguration>();
        
        // Bindings
        private ValueBinding<string> _resourceChainConfigBinding;
        private ValueBinding<string> _districtConfigBinding;
        private ValueBinding<bool> _buildingPickerActiveBinding;
        
        // Tool systems
        private BuildingPickerToolSystem _buildingPickerToolSystem;
        private ToolSystem _toolSystem;
        
        // State for tracking building picker
        private string _currentRuleId;
        private int _currentBuildingEntityId;
        private bool _isDistrictMode; // Track if we're working with a district or building
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("ResourceChainManagementSystem created");
            
            // Get tool systems
            _buildingPickerToolSystem = World.GetOrCreateSystemManaged<BuildingPickerToolSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            
            // Add binding to send configuration data to UI
            AddBinding(_resourceChainConfigBinding = new ValueBinding<string>("manageResourceChains", "resourceChainConfig", "{}"));
            AddBinding(_districtConfigBinding = new ValueBinding<string>("manageResourceChains", "districtConfig", "{}"));
            AddBinding(_buildingPickerActiveBinding = new ValueBinding<bool>("manageResourceChains", "buildingPickerActive", false));
            
            // Add method bindings for UI to call
            AddBinding(new TriggerBinding<int>("manageResourceChains", "requestBuildingConfig", RequestBuildingConfig));
            AddBinding(new TriggerBinding<int>("manageResourceChains", "requestDistrictConfig", RequestDistrictConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "saveBuildingConfig", SaveBuildingConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "saveDistrictConfig", SaveDistrictConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "addResourceChainRule", AddResourceChainRule));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "removeResourceChainRule", RemoveResourceChainRule));
            AddBinding(new TriggerBinding<int, string, string>("manageResourceChains", "updateResourceChainRule", UpdateResourceChainRule));
            
            // Building picker tool bindings
            AddBinding(new TriggerBinding<int, string, bool>("manageResourceChains", "startBuildingPicker", StartBuildingPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "confirmBuildingPicker", ConfirmBuildingPicker));
            AddBinding(new TriggerBinding("manageResourceChains", "cancelBuildingPicker", CancelBuildingPicker));
            
            // Load configurations from file
            LoadConfigurations();
        }

        protected override void OnUpdate()
        {
            // Nothing to update each frame
        }

        /// <summary>
        /// Request configuration for a specific building
        /// </summary>
        private void RequestBuildingConfig(int buildingEntityId)
        {
            try
            {
                Mod.log.Info($"Requesting config for building {buildingEntityId}");
                
                if (!_buildingConfigurations.ContainsKey(buildingEntityId))
                {
                    _buildingConfigurations[buildingEntityId] = new Data.BuildingConfiguration
                    {
                        BuildingEntityId = buildingEntityId,
                        Rules = new List<Data.ResourceChainRule>()
                    };
                }
                
                var config = _buildingConfigurations[buildingEntityId];
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                Mod.log.Info($"Sending config: {json}");
                _resourceChainConfigBinding.Update(json);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error requesting building config: {ex.Message}");
                _resourceChainConfigBinding.Update("{}");
            }
        }

        /// <summary>
        /// Save configuration for a building
        /// </summary>
        private void SaveBuildingConfig(int buildingEntityId, string configJson)
        {
            try
            {
                Mod.log.Info($"Saving config for building {buildingEntityId}: {configJson}");
                
                var config = JsonConvert.DeserializeObject<Data.BuildingConfiguration>(configJson);
                if (config != null)
                {
                    _buildingConfigurations[buildingEntityId] = config;
                    SaveConfigurations();
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving building config: {ex.Message}");
            }
        }

        /// <summary>
        /// Request configuration for a specific district
        /// </summary>
        private void RequestDistrictConfig(int districtEntityId)
        {
            try
            {
                Mod.log.Info($"Requesting config for district {districtEntityId}");
                
                if (!_districtConfigurations.ContainsKey(districtEntityId))
                {
                    _districtConfigurations[districtEntityId] = new Data.BuildingConfiguration
                    {
                        BuildingEntityId = districtEntityId,
                        Rules = new List<Data.ResourceChainRule>()
                    };
                }
                
                var config = _districtConfigurations[districtEntityId];
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                Mod.log.Info($"Sending district config: {json}");
                _districtConfigBinding.Update(json);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error requesting district config: {ex.Message}");
                _districtConfigBinding.Update("{}");
            }
        }

        /// <summary>
        /// Save configuration for a district
        /// </summary>
        private void SaveDistrictConfig(int districtEntityId, string configJson)
        {
            try
            {
                Mod.log.Info($"Saving config for district {districtEntityId}: {configJson}");
                
                var config = JsonConvert.DeserializeObject<Data.BuildingConfiguration>(configJson);
                if (config != null)
                {
                    _districtConfigurations[districtEntityId] = config;
                    SaveConfigurations();
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving district config: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a new resource chain rule
        /// </summary>
        private void AddResourceChainRule(int buildingEntityId, string ruleJson)
        {
            try
            {
                Mod.log.Info($"Adding rule for building {buildingEntityId}");
                
                if (!_buildingConfigurations.ContainsKey(buildingEntityId))
                {
                    _buildingConfigurations[buildingEntityId] = new Data.BuildingConfiguration
                    {
                        BuildingEntityId = buildingEntityId,
                        Rules = new List<Data.ResourceChainRule>()
                    };
                }
                
                var rule = JsonConvert.DeserializeObject<Data.ResourceChainRule>(ruleJson) ?? new Data.ResourceChainRule();
                _buildingConfigurations[buildingEntityId].Rules.Add(rule);
                
                SaveConfigurations();
                RequestBuildingConfig(buildingEntityId);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error adding rule: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove a resource chain rule
        /// </summary>
        private void RemoveResourceChainRule(int buildingEntityId, string ruleId)
        {
            try
            {
                Mod.log.Info($"Removing rule {ruleId} for building {buildingEntityId}");
                
                if (_buildingConfigurations.ContainsKey(buildingEntityId))
                {
                    var config = _buildingConfigurations[buildingEntityId];
                    config.Rules.RemoveAll(r => r.Id == ruleId);
                    
                    SaveConfigurations();
                    RequestBuildingConfig(buildingEntityId);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error removing rule: {ex.Message}");
            }
        }

        /// <summary>
        /// Update an existing resource chain rule
        /// </summary>
        private void UpdateResourceChainRule(int buildingEntityId, string ruleId, string ruleJson)
        {
            try
            {
                Mod.log.Info($"Updating rule {ruleId} for building {buildingEntityId}");
                
                if (_buildingConfigurations.ContainsKey(buildingEntityId))
                {
                    var config = _buildingConfigurations[buildingEntityId];
                    var existingRuleIndex = config.Rules.FindIndex(r => r.Id == ruleId);
                    
                    if (existingRuleIndex >= 0)
                    {
                        var updatedRule = JsonConvert.DeserializeObject<Data.ResourceChainRule>(ruleJson);
                        if (updatedRule != null)
                        {
                            config.Rules[existingRuleIndex] = updatedRule;
                            SaveConfigurations();
                            RequestBuildingConfig(buildingEntityId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error updating rule: {ex.Message}");
            }
        }

        /// <summary>
        /// Load configurations from file
        /// </summary>
        private void LoadConfigurations()
        {
            try
            {
                // Load building configs
                string buildingConfigPath = GetBuildingConfigFilePath();
                if (File.Exists(buildingConfigPath))
                {
                    string json = File.ReadAllText(buildingConfigPath);
                    var configs = JsonConvert.DeserializeObject<List<Data.BuildingConfiguration>>(json);
                    
                    if (configs != null)
                    {
                        _buildingConfigurations.Clear();
                        foreach (var config in configs)
                        {
                            _buildingConfigurations[config.BuildingEntityId] = config;
                        }
                        
                        Mod.log.Info($"Loaded {configs.Count} building configurations");
                    }
                }
                
                // Load district configs
                string districtConfigPath = GetDistrictConfigFilePath();
                if (File.Exists(districtConfigPath))
                {
                    string json = File.ReadAllText(districtConfigPath);
                    var configs = JsonConvert.DeserializeObject<List<Data.BuildingConfiguration>>(json);
                    
                    if (configs != null)
                    {
                        _districtConfigurations.Clear();
                        foreach (var config in configs)
                        {
                            _districtConfigurations[config.BuildingEntityId] = config;
                        }
                        
                        Mod.log.Info($"Loaded {configs.Count} district configurations");
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error loading configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all building configurations (for use by other systems like pathfinding)
        /// </summary>
        public Dictionary<int, Data.BuildingConfiguration> GetAllConfigurations()
        {
            return _buildingConfigurations;
        }

        /// <summary>
        /// Save configurations to file
        /// </summary>
        private void SaveConfigurations()
        {
            try
            {
                // Save building configs
                string buildingConfigPath = GetBuildingConfigFilePath();
                var buildingConfigs = _buildingConfigurations.Values.ToList();
                string buildingJson = JsonConvert.SerializeObject(buildingConfigs, Formatting.Indented);
                
                string directory = Path.GetDirectoryName(buildingConfigPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(buildingConfigPath, buildingJson);
                
                Mod.log.Info($"Saved {buildingConfigs.Count} building configurations");
                
                // Save district configs
                string districtConfigPath = GetDistrictConfigFilePath();
                var districtConfigs = _districtConfigurations.Values.ToList();
                string districtJson = JsonConvert.SerializeObject(districtConfigs, Formatting.Indented);
                File.WriteAllText(districtConfigPath, districtJson);
                
                Mod.log.Info($"Saved {districtConfigs.Count} district configurations");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the building configuration file path
        /// </summary>
        private string GetBuildingConfigFilePath()
        {
            // Save in the user's local application data folder
            string userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string modPath = Path.Combine(userDataPath, "Colossal Order", "Cities Skylines II", "ModsData", "ManageResourceChains");
            return Path.Combine(modPath, "resource_chain_configs.json");
        }

        /// <summary>
        /// Get the district configuration file path
        /// </summary>
        private string GetDistrictConfigFilePath()
        {
            // Save in the user's local application data folder
            string userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string modPath = Path.Combine(userDataPath, "Colossal Order", "Cities Skylines II", "ModsData", "ManageResourceChains");
            return Path.Combine(modPath, "district_chain_configs.json");
        }

        /// <summary>
        /// Start building picker tool
        /// </summary>
        private void StartBuildingPicker(int entityId, string ruleId, bool isDistrict)
        {
            try
            {
                string entityType = isDistrict ? "district" : "building";
                Mod.log.Info($"Starting building picker for {entityType} {entityId}, rule {ruleId}");
                _currentBuildingEntityId = entityId;
                _currentRuleId = ruleId;
                _isDistrictMode = isDistrict;
                
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
                string entityType = _isDistrictMode ? "district" : "building";
                Mod.log.Info($"Building {buildingId} selected for {entityType} {_currentBuildingEntityId}, updating UI");
                
                // Get the correct configuration dictionary based on mode
                var configDictionary = _isDistrictMode ? _districtConfigurations : _buildingConfigurations;
                var configBinding = _isDistrictMode ? _districtConfigBinding : _resourceChainConfigBinding;
                
                // Ensure config exists for this entity
                if (!configDictionary.TryGetValue(_currentBuildingEntityId, out var config))
                {
                    config = new Data.BuildingConfiguration
                    {
                        BuildingEntityId = _currentBuildingEntityId,
                        Rules = new List<Data.ResourceChainRule>()
                    };
                    configDictionary[_currentBuildingEntityId] = config;
                }
                
                // Find or create the rule
                var rule = config.Rules.FirstOrDefault(r => r.Id == _currentRuleId);
                if (rule == null)
                {
                    rule = new Data.ResourceChainRule
                    {
                        Id = _currentRuleId,
                        Color = "#FF0000",
                        Type = Data.ChainType.Incoming,
                        Allow = Data.AllowType.Allow,
                        TransportType = Data.TransportType.Resources,
                        Buildings = new List<int>(),
                        Districts = new List<int>(),
                        TransportPriorities = new List<Data.TransportPriority>()
                    };
                    config.Rules.Add(rule);
                }
                
                // Add building to the rule if not already present
                if (!rule.Buildings.Contains(buildingId))
                {
                    rule.Buildings.Add(buildingId);
                    Mod.log.Info($"✓ Added building {buildingId} to {entityType} rule {_currentRuleId}");
                    
                    // Send updated config to UI immediately
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    configBinding.Update(json);
                    
                    Mod.log.Info($"UI updated with new building for {entityType}");
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
                
                // Buildings are already added to the config via OnBuildingSelected
                // Just save and deactivate the picker
                SaveConfigurations();
                
                // Deactivate tool
                _buildingPickerToolSystem.ConfirmSelection();
                _buildingPickerActiveBinding.Update(false);
                
                Mod.log.Info("Building picker confirmed and saved");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error confirming building picker: {ex.Message}");
                Mod.log.Error($"Stack trace: {ex.StackTrace}");
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
                
                // Deactivate tool
                _buildingPickerToolSystem.CancelSelection();
                _buildingPickerActiveBinding.Update(false);
                
                Mod.log.Info("Building picker cancelled");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error cancelling building picker: {ex.Message}");
            }
        }

        public static Data.BuildingConfiguration GetBuildingConfiguration(int buildingEntityId)
        {
            if (_buildingConfigurations.ContainsKey(buildingEntityId))
            {
                return _buildingConfigurations[buildingEntityId];
            }
            
            return new Data.BuildingConfiguration
            {
                BuildingEntityId = buildingEntityId,
                Rules = new List<Data.ResourceChainRule>()
            };
        }
    }
}

