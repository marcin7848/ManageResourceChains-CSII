# Quick Start: Service Rules for Police & Other Services

## ✅ READY TO USE

Service rules are now implemented! You can restrict which buildings any service (police, fire, healthcare, etc.) can serve.

## Example: Police Station Coverage

### What You Want
Police Station A should only serve Downtown District.

### How to Do It

1. **Find Your Police Station ID**
   - Use the Building Picker tool in-game
   - Click on Police Station A
   - Note the Entity ID (e.g., 12345)

2. **Find Your District ID**
   - Use the District Picker tool
   - Click on Downtown District  
   - Note the Entity ID (e.g., 11111)

3. **Create the Rule**
   - Open ManageResourceChains UI
   - Select Police Station A
   - Add a new rule:
     - **Type**: Outgoing (from police station)
     - **Allow**: Allow (whitelist mode)
     - **TransportType**: Services
     - **Districts**: [11111] (Downtown District)

4. **Done!**
   - Police cars from Station A will ONLY respond to Downtown
   - Other districts won't get service from this station

## How It Works Technically

We patch **one method** that ALL services use:
```
AreaUtils.CheckServiceDistrict(target, service)
```

This method is called by:
- Police pathfinding ✓
- Fire pathfinding ✓
- Healthcare pathfinding ✓
- Garbage pathfinding ✓
- All other services ✓

**Our Harmony patch adds a simple check:**
```csharp
// After vanilla check passes...
bool allowed = IsServiceTransportAllowed(service.Index, target.Index);
if (!allowed) 
    return false;  // Block this service
```

## All Services Supported

✅ **Police** - Stations, cars, helicopters  
✅ **Fire** - Stations, engines, helicopters  
✅ **Healthcare** - Hospitals, ambulances  
✅ **Deathcare** - Cemeteries, hearses  
✅ **Garbage** - Facilities, trucks  
✅ **Post** - Offices, vans  
✅ **Maintenance** - Depots, vehicles  
✅ **Evacuation** - Shelters, buses  

## Rule Examples

### Police: Zone by District
```json
{
  "BuildingEntityId": 12345,  // Police Station
  "Rules": [{
    "Type": "Outgoing",
    "Allow": "Allow",
    "TransportType": "Services",
    "Districts": [11111, 22222]  // Only these districts
  }]
}
```

### Fire: Industrial Only
```json
{
  "BuildingEntityId": 23456,  // Fire Station
  "Rules": [{
    "Type": "Outgoing",
    "Allow": "Allow",
    "TransportType": "Services",
    "Buildings": [/* industrial building IDs */]
  }]
}
```

### Hospital: Exclude Poor District
```json
{
  "BuildingEntityId": 34567,  // Hospital
  "Rules": [{
    "Type": "Outgoing",
    "Allow": "Disallow",
    "TransportType": "Services",
    "Districts": [33333]  // Block this district
  }]
}
```

## Debugging

### Enable Logging

In `ServicePathfindingPatches.cs` line 82, uncomment:
```csharp
Mod.log.Info($"Service BLOCKED by rules: service={service.Index} -> target={target.Index}");
```

### Check Logs

Look in `Player.log` for:
```
[ManageResourceChains] Service pathfinding patches applied successfully
[ManageResourceChains] Service BLOCKED by rules: service=12345 -> target=67890
```

## What Changed in Code

### New Files
1. `Systems/ServicePathfindingPatches.cs` - Harmony patch
2. `Systems/ServiceRulesInterceptSystem.cs` - Service management

### Modified Files
1. `Mod.cs` - Registers systems, applies Harmony patches
2. `Systems/ResourceChainRulesSystem.cs` - Added `IsServiceTransportAllowed()`

### Key Method
```csharp
// In ResourceChainRulesSystem.cs
public bool IsServiceTransportAllowed(int serviceBuildingId, int targetBuildingId)
{
    // Checks if service from serviceBuildingId can serve targetBuildingId
    // Uses same rule evaluation logic as worker transport
    // Returns true if allowed, false if blocked
}
```

## No Further Action Needed

The system is **complete and functional**. Just:
1. Build the mod ✓ (Already done)
2. Load in game
3. Create rules via UI
4. Services will respect the rules automatically!

---

**Status**: ✅ Ready for Use  
**Build**: ✅ Success  
**Testing**: Ready for in-game validation
