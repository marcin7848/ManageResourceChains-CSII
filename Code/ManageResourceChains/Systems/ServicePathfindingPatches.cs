using System;
using System.Reflection;
using Game.Areas;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// Harmony patches to intercept service pathfinding and apply rules.
    /// 
    /// This patches AreaUtils.CheckServiceDistrict which is called by ALL service
    /// pathfinding setups (Police, Fire, Healthcare, Garbage, etc.) to determine
    /// if a service building can serve a target building.
    /// </summary>
    public static class ServicePathfindingPatches
    {
        private static ServiceRulesInterceptSystem s_InterceptSystem;
        private static ResourceChainRulesSystem s_RulesSystem;

        /// <summary>
        /// Initialize harmony patches for service pathfinding
        /// </summary>
        public static void Apply(Harmony harmony, ServiceRulesInterceptSystem interceptSystem, ResourceChainRulesSystem rulesSystem)
        {
            s_InterceptSystem = interceptSystem;
            s_RulesSystem = rulesSystem;

            try
            {
                Mod.log.Info("Attempting to patch all CheckServiceDistrict overloads to find which one is used...");
                
                // Get all methods named CheckServiceDistrict from AreaUtils
                var allMethods = typeof(AreaUtils).GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                );
                
                int patchCount = 0;
                
                // Patch ALL overloads to see which one actually gets called
                foreach (var method in allMethods)
                {
                    if (method.Name == "CheckServiceDistrict")
                    {
                        var parameters = method.GetParameters();
                        Mod.log.Info($"Found CheckServiceDistrict overload with {parameters.Length} parameters:");
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            Mod.log.Info($"  Param {i}: {parameters[i].Name} ({parameters[i].ParameterType.Name})");
                        }
                        
                        // Patch this overload with the appropriate postfix based on parameter count
                        try
                        {
                            System.Reflection.MethodInfo postfix = null;
                            
                            if (parameters.Length == 3 && parameters[0].Name == "district")
                            {
                                // Overload 1: (Entity district, Entity service, BufferLookup<ServiceDistrict>)
                                postfix = AccessTools.Method(typeof(ServicePathfindingPatches), nameof(CheckServiceDistrictPostfix_Overload1));
                            }
                            else if (parameters.Length == 4)
                            {
                                // Overload 2: (Entity district1, Entity district2, Entity service, BufferLookup<ServiceDistrict>)
                                postfix = AccessTools.Method(typeof(ServicePathfindingPatches), nameof(CheckServiceDistrictPostfix_Overload2));
                            }
                            else if (parameters.Length == 3 && parameters[0].Name == "building")
                            {
                                // Overload 3: (Entity building, DynamicBuffer<ServiceDistrict>, ref ComponentLookup<CurrentDistrict>)
                                postfix = AccessTools.Method(typeof(ServicePathfindingPatches), nameof(CheckServiceDistrictPostfix_Overload3));
                            }
                            
                            if (postfix != null)
                            {
                                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                                patchCount++;
                                Mod.log.Info($"  -> Patched successfully with {postfix.Name}!");
                            }
                            else
                            {
                                Mod.log.Warn($"  -> No matching postfix found for this overload");
                            }
                        }
                        catch (Exception ex)
                        {
                            Mod.log.Warn($"  -> Failed to patch: {ex.Message}");
                        }
                    }
                }
                
                Mod.log.Info($"Service pathfinding patches applied - patched {patchCount} CheckServiceDistrict overload(s)");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Failed to apply service pathfinding patches: {ex}");
            }
        }

        /// <summary>
        /// Postfix for overload 1: CheckServiceDistrict(Entity district, Entity service, BufferLookup<ServiceDistrict> serviceDistricts)
        /// This is the one used by police/fire/healthcare pathfinding
        /// </summary>
        private static void CheckServiceDistrictPostfix_Overload1(ref bool __result, Entity district, Entity service)
        {
            Mod.log.Info($"=== OVERLOAD 1 CALLED === district={district.Index}, service={service.Index}, result={__result}");
            
            if (!__result)
            {
                Mod.log.Info($"  -> Vanilla already blocked");
                return;
            }

            if (s_RulesSystem == null)
            {
                Mod.log.Info($"  -> RulesSystem not initialized");
                return;
            }

            try
            {
                // In police pathfinding: CheckServiceDistrict(entity2, entity, m_ServiceDistricts)
                // entity2 = target, entity = police station
                // So: district = target, service = police station
                bool allowed = s_RulesSystem.IsServiceTransportAllowed(service.Index, district.Index);
                Mod.log.Info($"  -> IsServiceTransportAllowed(service={service.Index}, target={district.Index}) = {allowed}");
                
                if (!allowed)
                {
                    __result = false;
                    Mod.log.Info($"  -> *** SERVICE BLOCKED BY RULES! ***");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error in service rules check: {ex}");
            }
        }

        /// <summary>
        /// Postfix for overload 2: CheckServiceDistrict(Entity district1, Entity district2, Entity service, BufferLookup<ServiceDistrict> serviceDistricts)
        /// </summary>
        private static void CheckServiceDistrictPostfix_Overload2(ref bool __result, Entity district1, Entity district2, Entity service)
        {
            Mod.log.Info($"=== OVERLOAD 2 CALLED === district1={district1.Index}, district2={district2.Index}, service={service.Index}, result={__result}");
            
            if (!__result || s_RulesSystem == null)
                return;

            try
            {
                // Check rules for both districts
                bool allowed1 = s_RulesSystem.IsServiceTransportAllowed(service.Index, district1.Index);
                bool allowed2 = s_RulesSystem.IsServiceTransportAllowed(service.Index, district2.Index);
                Mod.log.Info($"  -> allowed1={allowed1}, allowed2={allowed2}");
                
                if (!allowed1 && !allowed2)
                {
                    __result = false;
                    Mod.log.Info($"  -> *** SERVICE BLOCKED BY RULES! ***");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error in service rules check: {ex}");
            }
        }

        /// <summary>
        /// Postfix for overload 3: CheckServiceDistrict(Entity building, DynamicBuffer<ServiceDistrict> serviceDistricts, ref ComponentLookup<CurrentDistrict> currentDistricts)
        /// </summary>
        private static void CheckServiceDistrictPostfix_Overload3(ref bool __result, Entity building)
        {
            Mod.log.Info($"=== OVERLOAD 3 CALLED === building={building.Index}, result={__result}");
            
            // This overload doesn't have a service parameter, so we can't apply rules here
            // It's checking if a building is in service districts
        }

        /// <summary>
        /// Remove harmony patches
        /// </summary>
        public static void Remove(Harmony harmony)
        {
            try
            {
                // Use same reflection approach as Apply to find the exact method
                var allMethods = typeof(AreaUtils).GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                );
                
                System.Reflection.MethodInfo targetMethod = null;
                foreach (var method in allMethods)
                {
                    if (method.Name == "CheckServiceDistrict")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 3 &&
                            parameters[0].Name == "district" &&
                            parameters[1].Name == "service" &&
                            parameters[0].ParameterType == typeof(Entity) &&
                            parameters[1].ParameterType == typeof(Entity) &&
                            parameters[2].ParameterType == typeof(BufferLookup<ServiceDistrict>))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }
                
                if (targetMethod != null)
                {
                    harmony.Unpatch(targetMethod, HarmonyPatchType.All, harmony.Id);
                }
                
                Mod.log.Info("Service pathfinding patches removed");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Failed to remove service pathfinding patches: {ex}");
            }
        }
    }
}
