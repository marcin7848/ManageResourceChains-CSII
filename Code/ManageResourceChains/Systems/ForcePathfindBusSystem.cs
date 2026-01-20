using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Game.Pathfind;
using Game.Simulation;
using Unity.Entities;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// NUCLEAR OPTION: Use Harmony Transpiler to modify PathfindParameters.m_Methods
    /// directly in TripNeededSystem to remove Track method (trains) when bus preference is enabled.
    /// 
    /// This is the ONLY way to truly force bus-only transport because:
    /// 1. PathMethod.PublicTransportDay includes all public transport
    /// 2. PathMethod.Track (0x10) is used for trains/trams
    /// 3. We can remove Track from the methods bitfield
    /// </summary>
    public static class ForcePathfindBusPatches
    {
        private static Harmony _harmony;
        
        public static void ApplyPatches()
        {
            try
            {
                _harmony = new Harmony("ManageResourceChains.ForcePathfindBus");
                
                // We need to patch the method in TripNeededSystem where PathfindParameters is created
                // This is in the CitizenJob nested struct's Execute method
                var tripNeededSystemType = typeof(TripNeededSystem);
                
                // Get all nested types
                var nestedTypes = tripNeededSystemType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
                
                foreach (var nestedType in nestedTypes)
                {
                    // Look for the job struct that has Execute method
                    if (nestedType.Name.Contains("Job") || nestedType.Name.Contains("Citizen"))
                    {
                        var executeMethod = nestedType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
                        if (executeMethod != null)
                        {
                            try
                            {
                                var transpiler = typeof(ForcePathfindBusPatches).GetMethod(
                                    nameof(PathfindParametersTranspiler),
                                    BindingFlags.Static | BindingFlags.NonPublic);
                                
                                _harmony.Patch(executeMethod, transpiler: new HarmonyMethod(transpiler));
                                Mod.log.Info($"Patched {nestedType.Name}.Execute with transpiler to force bus pathfinding");
                            }
                            catch (Exception ex)
                            {
                                Mod.log.Warn($"Failed to patch {nestedType.Name}.Execute: {ex.Message}");
                            }
                        }
                    }
                }
                
                Mod.log.Info("ForcePathfindBus transpiler patches applied");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error applying ForcePathfindBus patches: {ex.Message}");
                Mod.log.Error(ex.StackTrace);
            }
        }
        
        public static void RemovePatches()
        {
            try
            {
                _harmony?.UnpatchAll("ManageResourceChains.ForcePathfindBus");
                Mod.log.Info("Removed ForcePathfindBus patches");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error removing ForcePathfindBus patches: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Transpiler that injects code after PathfindParameters.m_Methods is set
        /// to remove the Track flag (trains) if bus preference is enabled.
        /// 
        /// Original IL:
        ///   ldloca.s parameters
        ///   ldc.i4.s 8  // PathMethod.PublicTransportDay
        ///   stfld PathfindParameters::m_Methods
        /// 
        /// We inject after this:
        ///   ldloca.s parameters
        ///   call RemoveTrackMethod
        /// </summary>
        private static IEnumerable<CodeInstruction> PathfindParametersTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var fieldInfo = typeof(PathfindParameters).GetField("m_Methods");
            var removeTrackMethod = typeof(ForcePathfindBusPatches).GetMethod(
                nameof(RemoveTrackFromMethods),
                BindingFlags.Static | BindingFlags.Public);
            
            bool patched = false;
            
            for (int i = 0; i < codes.Count; i++)
            {
                // Look for stfld PathfindParameters::m_Methods
                if (codes[i].StoresField(fieldInfo))
                {
                    // Get the local variable index for 'parameters'
                    // It should be in the instruction before the one before stfld
                    int localIndex = -1;
                    if (i >= 2 && codes[i - 2].IsLdloc())
                    {
                        localIndex = codes[i - 2].operand as int? ?? -1;
                    }
                    else if (i >= 2 && codes[i - 2].opcode == OpCodes.Ldloca_S)
                    {
                        localIndex = codes[i - 2].operand as int? ?? -1;
                    }
                    
                    if (localIndex >= 0)
                    {
                        // Insert our call right after stfld
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, removeTrackMethod));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldloca_S, localIndex));
                        patched = true;
                        Mod.log.Info($"Injected RemoveTrackFromMethods call at position {i + 1}");
                        i += 2; // Skip the inserted instructions
                    }
                }
            }
            
            if (!patched)
            {
                Mod.log.Warn("Could not find PathfindParameters.m_Methods assignment in transpiler");
            }
            
            return codes;
        }
        
        /// <summary>
        /// Called by injected IL code to modify PathfindParameters.m_Methods
        /// to remove Track flag if bus preference is enabled.
        /// </summary>
        public static void RemoveTrackFromMethods(ref PathfindParameters parameters)
        {
            var preference = TransportPreferenceSystem.DefaultPreference;
            
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.Bus)
            {
                // Remove Track (0x10) from methods
                // Track is used for trains and trams
                parameters.m_Methods &= ~PathMethod.Track;
                
                // Also make sure we have PublicTransportDay/Night for buses
                parameters.m_Methods |= PathMethod.PublicTransportDay;
                parameters.m_Methods |= PathMethod.PublicTransportNight;
            }
        }
    }
}
