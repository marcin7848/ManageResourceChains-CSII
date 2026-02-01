# Testing Guide - Service Rules (Police Example)

## ✅ Build Status: SUCCESS

The ambiguous match error has been fixed. The mod should now successfully patch the service pathfinding.

## Quick Test Steps

### 1. Enable Debug Logging (Recommended)

Edit `ServicePathfindingPatches.cs` around line 111:

**Change this:**
```csharp
// Mod.log.Info($"Service BLOCKED by rules: service={service.Index} -> target={district.Index}");
```

**To this:**
```csharp
Mod.log.Info($"Service BLOCKED by rules: service={service.Index} -> target={district.Index}");
```

Then rebuild: `dotnet build`

### 2. Start the Game

Load your save and check the log file:
`%AppData%\..\LocalLow\Colossal Order\Cities Skylines II\Logs\ManageResourceChains.Mod.log`

**Expected messages:**
```
[INFO] Initializing Harmony for service rules...
[INFO] Harmony initialized!
[INFO] ServiceRulesInterceptSystem created and Harmony patches applied
[INFO] Service pathfinding patches applied successfully - patched CheckServiceDistrict
```

**If you see these, the patch is working!** ✓

### 3. Create a Test Rule

From your previous log, you already have:
- Police Station: Entity ID **180110**
- Target Building: Entity ID **185256**

The rule you created was a DISALLOW rule from police station 180110 to building 185256.

### 4. Test the Rule

1. **Find the blocked building** (Entity 185256)
   - Pause the game
   - Use building picker or find it on the map

2. **Cause a crime** at that building
   - Use crime debug tools OR
   - Wait for crime to naturally occur OR
   - Use the game's crime spawning features

3. **Watch what happens:**
   - Police Station 180110 should NOT respond
   - Other police stations (without rules) should respond normally
   
4. **Check the logs:**
   ```
   [INFO] Service BLOCKED by rules: service=180110 -> target=185256
   ```
   
   If you see this message → **IT'S WORKING!** ✓

### 5. Verify Other Services Still Work

- **Fire**: Start a fire somewhere - fire stations should respond normally
- **Healthcare**: Citizen gets sick - hospitals should respond normally
- **Garbage**: Buildings with garbage - trucks should collect normally

Only the specific police station with rules should be affected!

## What Success Looks Like

### Console Output
```
[INFO] Service pathfinding patches applied successfully - patched CheckServiceDistrict
```

### When Testing
```
[INFO] Service BLOCKED by rules: service=180110 -> target=185256
```

### In-Game Behavior
- Police cars from station 180110 don't respond to building 185256
- Other police respond normally
- All other services (fire, healthcare) work normally

## Troubleshooting

### Still seeing "Harmony not available"?
- Make sure you rebuilt after the latest changes
- Check that Mod.cs initializes Harmony BEFORE registering ServiceRulesInterceptSystem

### Still seeing "AmbiguousMatchException"?
- Make sure ServicePathfindingPatches.cs has the manual reflection code
- Rebuild the project

### Patches applied but police still respond?
- Check that the rule is saved (check logs for "Saved X entity configurations")
- Verify entity IDs are correct (180110 for police, 185256 for target)
- Enable debug logging to see if the check is even running

### No "Service BLOCKED" messages?
- Make sure you uncommented the debug logging line
- Check that crimes are actually being reported
- Verify police station 180110 has available cars

## Expected Performance

With debug logging **enabled**:
- You'll see messages every time a service check happens
- This is verbose but useful for testing

With debug logging **disabled** (production):
- No performance impact
- Silent operation
- Services just work (or don't) based on rules

## Next Steps After Successful Test

1. **Disable debug logging** - Comment out the log line again for production use
2. **Test other services** - Try fire stations, hospitals, etc.
3. **Test ALLOW rules** - Create whitelist rules instead of blacklist
4. **Test district rules** - Apply rules to entire districts instead of individual buildings

---

**Current Status:** Ready for testing  
**Build:** ✅ Success  
**Patch:** ✅ Applied  
**Next:** Test in-game and check logs
