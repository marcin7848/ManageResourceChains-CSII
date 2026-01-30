using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using ManageResourceChains.Systems;

namespace ManageResourceChains
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ManageResourceChains)}.{nameof(Mod)}")
            .SetShowsErrorsInUI(false);

        public static ILog Log => log;

        // Input action constants for building picker tool
        public const string kToolConfirmAction = "Tool Confirm";
        public const string kToolCancelAction = "Tool Cancel";

        public static Setting Settings { get; private set; }

        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            try
            {
                if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                    log.Info($"Current mod asset at {asset.path}");

                m_Setting = new Setting(this);
                m_Setting.RegisterInOptionsUI();
                Settings = m_Setting;
                GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

                AssetDatabase.global.LoadSettings(nameof(ManageResourceChains), m_Setting, new Setting(this));

                // Register our UI systems
                log.Info("Registering BuildingSelectionUISystem...");
                updateSystem.UpdateAt<BuildingSelectionUISystem>(SystemUpdatePhase.UIUpdate);
                log.Info("BuildingSelectionUISystem registered!");

                log.Info("Registering DistrictSelectionUISystem...");
                updateSystem.UpdateAt<DistrictSelectionUISystem>(SystemUpdatePhase.UIUpdate);
                log.Info("DistrictSelectionUISystem registered!");

                log.Info("Registering ResourceChainManagementSystem...");
                updateSystem.UpdateAt<ResourceChainManagementSystem>(SystemUpdatePhase.UIUpdate);
                log.Info("ResourceChainManagementSystem registered!");

                // Register pathfind system for worker restrictions
                log.Info("Registering ResourceChainRulesSystem...");
                updateSystem.UpdateAt<ResourceChainRulesSystem>(SystemUpdatePhase.GameSimulation);
                log.Info("ResourceChainRulesSystem registered!");

                // Register building picker tool
                log.Info("Registering BuildingPickerToolSystem...");
                updateSystem.UpdateAt<BuildingPickerToolSystem>(SystemUpdatePhase.ToolUpdate);
                log.Info("BuildingPickerToolSystem registered!");

                // Register district picker tool
                log.Info("Registering DistrictPickerToolSystem...");
                updateSystem.UpdateAt<DistrictPickerToolSystem>(SystemUpdatePhase.ToolUpdate);
                log.Info("DistrictPickerToolSystem registered!");

                // Register trasnport preference system
                log.Info("Registering TransportPreferenceSystem...");
                updateSystem.UpdateAt<TransportPreferenceSystem>(SystemUpdatePhase.ToolUpdate);
                log.Info("TransportPreferenceSystem registered!");
                
            }
            catch (Exception ex)
            {
                log.Error($"Error in OnLoad: {ex.Message}");
                log.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            // Remove Harmony patches
            TransportPreferencePatches.RemovePatches();

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}