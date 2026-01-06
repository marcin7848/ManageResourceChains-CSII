using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.UI.Binding;
using Game.UI;
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
        
        // Bindings
        private ValueBinding<string> _resourceChainConfigBinding;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("ResourceChainManagementSystem created");
            
            // Add binding to send configuration data to UI
            AddBinding(_resourceChainConfigBinding = new ValueBinding<string>("manageResourceChains", "resourceChainConfig", "{}"));
            
            // Add method bindings for UI to call
            AddBinding(new TriggerBinding<int>("manageResourceChains", "requestBuildingConfig", RequestBuildingConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "saveBuildingConfig", SaveBuildingConfig));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "addResourceChainRule", AddResourceChainRule));
            AddBinding(new TriggerBinding<int, string>("manageResourceChains", "removeResourceChainRule", RemoveResourceChainRule));
            AddBinding(new TriggerBinding<int, string, string>("manageResourceChains", "updateResourceChainRule", UpdateResourceChainRule));
            
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
                string configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
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
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error loading configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Save configurations to file
        /// </summary>
        private void SaveConfigurations()
        {
            try
            {
                string configPath = GetConfigFilePath();
                var configs = _buildingConfigurations.Values.ToList();
                string json = JsonConvert.SerializeObject(configs, Formatting.Indented);
                
                string directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(configPath, json);
                
                Mod.log.Info($"Saved {configs.Count} building configurations");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error saving configurations: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the configuration file path
        /// </summary>
        private string GetConfigFilePath()
        {
            // Save in the user's local application data folder
            string userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string modPath = Path.Combine(userDataPath, "Colossal Order", "Cities Skylines II", "ModsData", "ManageResourceChains");
            return Path.Combine(modPath, "resource_chain_configs.json");
        }

        /// <summary>
        /// Get configuration for a specific building (can be called from other systems)
        /// </summary>
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

