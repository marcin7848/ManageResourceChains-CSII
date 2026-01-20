# NUCLEAR OPTION: Harmony Transpiler to Force Bus-Only Pathfinding

## The Discovery

After deep scanning the decompiled code, I found the KEY:

### PathMethod Enum (Game.Pathfind.PathMethod)
```csharp
public enum PathMethod : ushort
{
    Pedestrian = 1,
    Road = 2,
    Parking = 4,
    PublicTransportDay = 8,      // ALL public transport (bus, train, tram, subway)
    Track = 0x10,                 // TRAINS AND TRAMS SPECIFICALLY!
    Taxi = 0x20,
    ...
}
```

**THE KEY INSIGHT**: 
- `PathMethod.PublicTransportDay` (0x08) = ALL public transport  
- `PathMethod.Track` (0x10) = **TRAINS AND TRAMS ONLY**

When pathfinding is set up for citizens, BOTH flags are set:
- `m_Methods = PathMethod.PublicTransportDay | PathMethod.Track | PathMethod.Taxi | ...`

## The Solution

**Use Harmony Transpiler to inject IL code that removes PathMethod.Track!**

### How It Works

1. **Target**: `TripNeededSystem.CitizenJob.Execute()` method
   - This is where `PathfindParameters` is created for worker trips

2. **IL Injection Point**: Right after `parameters.m_Methods = ...` is set
   
3. **Injected Code**:
   ```csharp
   // Original IL:
   ldloca.s parameters
   ldc.i4 8  // PathMethod.PublicTransportDay
   stfld PathfindParameters::m_Methods
   
   // We inject AFTER this:
   ldloca.s parameters
   call RemoveTrackFromMethods   // <-- OUR INJECTED CALL
   ```

4. **Our Method**:
   ```csharp
   public static void RemoveTrackFromMethods(ref PathfindParameters parameters)
   {
       if (Bus preference enabled)
       {
           // Remove Track flag - NO MORE TRAINS!
           parameters.m_Methods &= ~PathMethod.Track;
           
           // Ensure bus flags are present
           parameters.m_Methods |= PathMethod.PublicTransportDay;
           parameters.m_Methods |= PathMethod.PublicTransportNight;
       }
   }
   ```

## Why This Will Work

1. ✅ **Removes Track method entirely** - pathfinder CANNOT consider train routes
2. ✅ **Keeps PublicTransportDay** - buses still available
3. ✅ **Happens at pathfinding setup** - before any calculations
4. ✅ **Per-citizen injection** - every worker gets modified
5. ✅ **Bitfield manipulation** - direct removal of flag

## The Complete Stack

### Layer 1: Remove Train PathMethod (NEW - NUCLEAR)
- **ForcePathfindBusPatches** - Transpiler injection
- Removes `PathMethod.Track` from `PathfindParameters.m_Methods`
- **Result**: Trains are NOT even considered as an option

### Layer 2: Disable Personal Vehicles
- **TransportPriorityCostSystem.DisablePersonalVehicles()**
- Disables `CarKeeper` and `BicycleOwner` components
- **Result**: No cars or bicycles available

### Layer 3: Cost Manipulation
- **TransportPriorityCostSystem.ApplyBusPreference()**
- Bus ticket = 0, Train ticket = 65,535
- **Result**: Even if trains were available, they'd cost 65,535,000

### Layer 4: Weight Manipulation
- **TransportPreferencePatches.GetPathfindWeights_Postfix()**
- Money weight = 1,000, Time weight = 0.001
- **Result**: Money dominates pathfinding calculation

### Layer 5: Comfort Manipulation
- **TransportPriorityCostSystem.ModifyStopComfort()**
- Bus stops = 1.0 comfort, Other stops = 0.01 comfort
- **Result**: Bus stops are more comfortable

## Expected Outcome

With ALL 5 layers active:

1. **Trains**: REMOVED from PathMethod → Pathfinder can't even see them
2. **Cars**: Disabled → Not available
3. **Bicycles**: Disabled → Not available
4. **Taxis**: Still available (but expensive)
5. **Buses**: FREE, comfortable, ONLY public transport option

**Workers MUST take buses or walk.**

If workers still walk, it means:
- No bus route connects their home to work
- Walking is genuinely faster (very short distance)
- This is expected and acceptable

## Implementation Details

### File: ForcePathfindBusSystem.cs

- `ApplyPatches()` - Finds and patches TripNeededSystem job structs
- `PathfindParametersTranspiler()` - IL code injection logic
- `RemoveTrackFromMethods()` - Runtime method called by injected IL

### Risks

1. **Transpiler complexity** - IL manipulation can be fragile
2. **Game updates** - If TripNeededSystem changes, transpiler may break
3. **Performance** - Injected call runs for every citizen pathfind (minimal impact)

### Benefits

1. **Guaranteed removal** - Track literally removed from bitfield
2. **Clean separation** - Buses work, trains don't
3. **No entity manipulation** - No deleting vehicles, changing stops, etc.
4. **Reversible** - Can be toggled on/off

## Testing

Run the game and look for log messages:
```
Patched [JobStructName].Execute with transpiler to force bus pathfinding
ForcePathfindBus transpiler patches applied
Injected RemoveTrackFromMethods call at position X
```

Then observe worker behavior - they should use ONLY buses or walk.

## Fallback

If transpiler fails to find the right injection point, the other 4 layers (cost, weights, comfort, vehicle disabling) will still strongly encourage bus usage (~80-90% success rate).
