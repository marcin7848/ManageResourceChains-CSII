using System;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
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
        private Harmony m_Harmony;
        
        // Static Harmony instance that can be accessed by systems
        public static Harmony HarmonyInstance { get; private set; }

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

                // Initialize Harmony FIRST - before registering systems that need it
                log.Info("Initializing Harmony for service rules...");
                m_Harmony = new Harmony("ManageResourceChains.ServiceRules");
                HarmonyInstance = m_Harmony;
                log.Info("Harmony initialized!");

                // Register pathfind system for worker restrictions
                log.Info("Registering ResourceChainRulesSystem...");
                updateSystem.UpdateAt<ResourceChainRulesSystem>(SystemUpdatePhase.GameSimulation);
                log.Info("ResourceChainRulesSystem registered!");

                // Register service rules intercept system (will apply Harmony patches in OnCreate)
                log.Info("Registering ServiceRulesInterceptSystem...");
                updateSystem.UpdateAt<ServiceRulesInterceptSystem>(SystemUpdatePhase.GameSimulation);
                log.Info("ServiceRulesInterceptSystem registered!");

                // Register service district manipulation system (works with Burst-compiled code)
                log.Info("Registering ServiceDistrictManipulationSystem...");
                updateSystem.UpdateAt<ServiceDistrictManipulationSystem>(SystemUpdatePhase.GameSimulation);
                log.Info("ServiceDistrictManipulationSystem registered!");

                // Register building picker tool
                log.Info("Registering BuildingPickerToolSystem...");
                updateSystem.UpdateAt<BuildingPickerToolSystem>(SystemUpdatePhase.ToolUpdate);
                log.Info("BuildingPickerToolSystem registered!");

                // Register district picker tool
                log.Info("Registering DistrictPickerToolSystem...");
                updateSystem.UpdateAt<DistrictPickerToolSystem>(SystemUpdatePhase.ToolUpdate);
                log.Info("DistrictPickerToolSystem registered!");
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
            if (m_Harmony != null)
            {
                try
                {
                    ServicePathfindingPatches.Remove(m_Harmony);
                    m_Harmony.UnpatchAll(m_Harmony.Id);
                    log.Info("Harmony patches removed");
                }
                catch (Exception ex)
                {
                    log.Error($"Error removing Harmony patches: {ex}");
                }
                HarmonyInstance = null;
                m_Harmony = null;
            }

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}