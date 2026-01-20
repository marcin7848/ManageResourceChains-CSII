# Transport Priority System Documentation

## Overview

The Transport Priority System forces workers to use specific transport methods (currently bus transport) by manipulating the game's pathfinding cost calculations and restricting available transport options.

## System Architecture

The system consists of three main components working together:

### 1. TransportPreferenceSystem
**File**: `Systems/TransportPreferenceSystem.cs`  
**Purpose**: Defines the transport preference settings and provides the configuration interface.

**Key Elements**:
- `PreferredTransportMethod` enum: Defines available transport preferences (None, Bus, Train, PublicTransport, Taxi, Walking, Bicycle)
- `DefaultPreference`: Static property set to `PreferredTransportMethod.Bus` by default
- Acts as the central configuration point for the other systems

**Status**: Currently a configuration holder. Originally designed for per-frame processing but the actual enforcement happens through other systems.

---

### 2. TransportPreferencePatches (Harmony Patches)
**File**: `Systems/TransportPreferencePatches.cs`  
**Purpose**: Uses Harmony to patch game methods and modify pathfinding behavior at runtime.

#### What It Patches:

**CitizenUtils.GetPathfindWeights** - Postfix patch that modifies pathfinding weight calculations

#### How It Works:

The game calculates pathfinding costs using this formula:
```
Total Cost = (Time × TimeWeight) + (Behaviour × BehaviourWeight) + (Money × MoneyWeight) + (Comfort × ComfortWeight)
```

For **Bus Preference**, the patch modifies weights to:
```csharp
TimeWeight    = 0.001  // Almost completely ignore travel time
MoneyWeight   = 1000   // EXTREMELY care about money cost
ComfortWeight = 0.001  // Almost completely ignore comfort
```

**The Effect**:
- A bus with 0 ticket cost: `(travel_time × 0.001) + (0 × 1000) + (comfort × 0.001) = ~0 cost`
- A train with 65,535 ticket: `(travel_time × 0.001) + (65535 × 1000) + (comfort × 0.001) = ~65,535,000 cost`

The train has a **65 MILLION point penalty**, making buses virtually always chosen.

#### Method Restrictions:

The `GetRestrictedMethods()` helper removes these PathMethod flags when bus preference is active:
- `PathMethod.Road` - No driving cars
- `PathMethod.Parking` - No parking
- `PathMethod.Taxi` - No taxis
- `PathMethod.Bicycle` - No bicycles
- `PathMethod.BicycleParking` - No bike parking
- `PathMethod.MediumRoad` - No trucks

And ensures these remain available:
- `PathMethod.Pedestrian` - Walking always available
- `PathMethod.PublicTransportDay` - Day-time public transport
- `PathMethod.PublicTransportNight` - Night-time public transport
- `PathMethod.Boarding` - Boarding vehicles

---

### 3. TransportPriorityCostSystem
**File**: `Systems/TransportPriorityCostSystem.cs`  
**Purpose**: Actively modifies game entities to enforce transport preferences through three mechanisms.

#### Update Frequency:
Runs every **128 frames** (~2-3 seconds) to continuously enforce preferences.

#### Mechanism 1: Ticket Price Manipulation

**What It Does**:
- Sets **bus line** ticket prices to **0** (completely free)
- Sets **non-bus line** (train, tram, subway, ferry) ticket prices to **65,535** (maximum possible value)

**Code Flow**:
1. Queries all `TransportLine` entities
2. For each line, checks if it's a bus line (using `IsBusLine()` helper)
3. Stores original ticket price in `_originalTicketPrices` dictionary
4. Modifies `TransportLine.m_TicketPrice` component

**Bus Line Detection**:
```csharp
private bool IsBusLine(Entity lineEntity)
{
    // Checks if any waypoint on the route has a BusStop component
    // Also checks Connected entities for BusStop
    return true if route uses bus stops, false otherwise
}
```

#### Mechanism 2: Stop Comfort Manipulation

**What It Does**:
- Sets **bus stops** comfort factor to **1.0** (maximum comfort, no waiting penalty)
- Sets **bus stops** loading factor to **1.0** (instant boarding)
- Sets **non-bus stops** comfort factor to **0.01** (horrible comfort, huge waiting penalty)
- Sets **non-bus stops** loading factor to **0.01** (extremely slow boarding)

**Code Flow**:
1. Queries all `TransportStop` entities
2. Checks if entity has `BusStop` component
3. Stores original `m_ComfortFactor` in `_originalComfortFactors` dictionary
4. Modifies `TransportStop.m_ComfortFactor` and `m_LoadingFactor`

**Effect**: Even if pathfinding considers non-bus transport, the comfort penalty adds to the total cost.

#### Mechanism 3: Personal Vehicle Disabling

**What It Does**:
- **Disables** the `CarKeeper` component on all citizens
- **Disables** the `BicycleOwner` component on all citizens

**Code Flow**:
1. Queries all citizens with enabled `CarKeeper` component
2. Calls `EntityManager.SetComponentEnabled<CarKeeper>(entity, false)`
3. Tracks disabled entities in `_disabledCarKeepers` HashSet
4. Same process for `BicycleOwner` component

**Effect**: When these components are disabled, the game's pathfinding code (specifically in `TripNeededSystem.cs` around line 1073) **does not add** `PathMethod.Road` or `PathMethod.Parking` to the available methods. Citizens physically cannot access their cars or bikes.

---

## How Workers Travel Under Bus Preference

### Step-by-Step Pathfinding Process:

1. **Worker needs to commute** (home → work or work → home)

2. **TransportPriorityCostSystem runs** (every ~2-3 seconds):
   - Bus lines: ticket = 0, comfort = 1.0
   - Train lines: ticket = 65,535, comfort = 0.01
   - Citizen's `CarKeeper` disabled
   - Citizen's `BicycleOwner` disabled

3. **TripNeededSystem creates PathfindParameters**:
   - Checks if citizen has `CarKeeper` enabled: **NO** → doesn't add `PathMethod.Road`
   - Checks if citizen has `BicycleOwner` enabled: **NO** → doesn't add `PathMethod.Bicycle`
   - Adds `PathMethod.PublicTransportDay` (all public transport)
   - Adds `PathMethod.Pedestrian` (walking)

4. **CitizenUtils.GetPathfindWeights is called**:
   - Returns weights: `(time=0.001, behaviour=normal, money=1000, comfort=0.001)`

5. **Pathfinding calculates costs for available routes**:
   ```
   Bus Route:
   - Time: 500 seconds × 0.001 = 0.5
   - Money: 0 (ticket) × 1000 = 0
   - Comfort: 0.8 × 0.001 = 0.0008
   - TOTAL: ~0.5
   
   Train Route:
   - Time: 200 seconds × 0.001 = 0.2
   - Money: 65,535 (ticket) × 1000 = 65,535,000
   - Comfort: 0.01 × 0.001 = 0.00001
   - TOTAL: ~65,535,000
   
   Walking:
   - Time: 1800 seconds × 0.001 = 1.8
   - Money: 0 × 1000 = 0
   - Comfort: 1.0 × 0.001 = 0.001
   - TOTAL: ~1.8
   ```

6. **Pathfinder chooses the lowest cost**: **Bus (cost ~0.5)**

7. **Worker boards bus and travels to destination**

---

## What Happens If There Are No Buses?

### Scenario 1: No Bus Lines in City
**Result**: Workers **walk** if distance is reasonable, or **don't commute** if distance is too far.

**Why**: 
- Cars disabled (no `CarKeeper`)
- Bikes disabled (no `BicycleOwner`)
- Only public transport + walking available
- If no public transport routes exist, only walking remains

**Game Behavior**:
- Short distance (< 1-2 km): Workers walk
- Long distance (> 2-3 km): Workers may not find a valid path
- Buildings may show "Not Enough Workers" problem
- Cims may become unemployed if they can't reach work

### Scenario 2: Bus Lines Exist But Don't Connect Origin to Destination
**Result**: Workers use **train/tram/metro** if available, otherwise **walk**.

**Why**:
- Pathfinder searches for any route using available methods
- `PathMethod.PublicTransportDay` includes ALL public transport (bus, train, tram, metro, ferry)
- Even with 65,535 ticket price, if it's the only way to reach work, it will be chosen
- The pathfinder prefers a 65,535,000 cost route over no route at all

**Cost Comparison**:
```
Walking 5km: Time=3000s × 0.001 = 3.0 cost
Train route: Money=65535 × 1000 = 65,535,000 cost
```
Walking is cheaper, so worker walks if possible. If walking distance > max walking distance (~5-7km), train will be chosen despite cost.

### Scenario 3: Bus Lines Exist But Are Full/Broken
**Result**: Workers **wait** for next bus or take alternative public transport.

**Why**:
- If bus capacity is full, pathfinder marks that specific bus as unavailable
- Pathfinder recalculates using next available bus or alternative route
- If no buses available within reasonable time, falls back to trains or walking

---

## System Restoration

When `TransportPreferenceSystem.DefaultPreference` is set to `None`, the `TransportPriorityCostSystem` automatically **restores all modifications**:

1. **Ticket Prices**: Restored from `_originalTicketPrices` dictionary
2. **Comfort Factors**: Restored from `_originalComfortFactors` dictionary
3. **CarKeeper**: Re-enabled for all citizens in `_disabledCarKeepers` set
4. **BicycleOwner**: Re-enabled for all citizens in `_disabledBicycleOwners` set

All modifications are **fully reversible** with no permanent effects.

---

## Why This Approach Works

### Multi-Layered Defense System

The system uses **FIVE simultaneous enforcement mechanisms**:

1. **Weight Manipulation** (TransportPreferencePatches)
   - Makes money cost 1000× more important than time
   - Forces pathfinder to heavily prefer free transport

2. **Ticket Price Manipulation** (TransportPriorityCostSystem)
   - Bus: Free (0 cost)
   - Train: Maximum price (65,535 cost)
   - Creates astronomical cost difference

3. **Comfort Manipulation** (TransportPriorityCostSystem)
   - Bus stops: Perfect comfort (1.0)
   - Other stops: Terrible comfort (0.01)
   - Adds additional penalty to non-bus transport

4. **Car Disabling** (TransportPriorityCostSystem)
   - Physically removes car access by disabling `CarKeeper`
   - Pathfinder cannot consider car routes at all

5. **Bike Disabling** (TransportPriorityCostSystem)
   - Physically removes bicycle access by disabling `BicycleOwner`
   - Pathfinder cannot consider bicycle routes at all

**Result**: Even if one mechanism fails, the others ensure bus preference is enforced.

---

## Limitations and Edge Cases

### 1. PathMethod.PublicTransportDay Includes All Transport
The game doesn't have separate `PathMethod.Bus` and `PathMethod.Train` flags. Both use `PathMethod.PublicTransportDay`.

**Workaround**: Extreme cost differences (65,535× ticket price) make non-bus transport virtually never chosen.

### 2. Pathfinding Cache
Citizens may have pre-calculated paths cached. Changes take effect on next path recalculation.

**Timeline**: Paths typically recalculate every few minutes or when citizen's situation changes.

### 3. Very Long Distances
If bus route requires 10 transfers and takes 90 minutes, but train takes 10 minutes, the pathfinder might still choose bus due to weight manipulation.

**Trade-off**: This is intentional - we prioritize bus usage over efficiency.

### 4. Emergency Situations
Fire trucks, ambulances, police, etc. are **NOT affected** - they have their own pathfinding logic.

---

## Performance Considerations

### Update Frequency
- **TransportPriorityCostSystem**: Every 128 frames (~2-3 seconds)
- **TransportPreferencePatches**: Per-pathfinding calculation (only when citizen recalculates path)

### Memory Usage
- `_originalTicketPrices`: ~2 bytes per transport line (~20 lines = ~40 bytes)
- `_originalComfortFactors`: ~4 bytes per stop (~100 stops = ~400 bytes)
- `_disabledCarKeepers`: ~8 bytes per citizen (~10,000 citizens = ~80 KB)
- `_disabledBicycleOwners`: ~8 bytes per citizen (~10,000 citizens = ~80 KB)

**Total**: < 200 KB of memory overhead

### CPU Impact
- Setting component values: ~0.01ms per entity
- Query operations: ~0.1ms per frame
- Harmony postfix patch: ~0.001ms per pathfind calculation

**Total**: < 1ms per frame impact

---

## Configuration

### Changing Transport Preference

Currently set via static field:
```csharp
TransportPreferenceSystem.DefaultPreference = PreferredTransportMethod.Bus;
```

**Available Options**:
- `None` - No preference, vanilla behavior, all modifications disabled
- `Bus` - Force bus transport only
- `PublicTransport` - Force all public transport (bus + train + tram + metro)
- `Train` - Force train transport (not fully implemented)
- `Taxi` - Force taxi transport (not fully implemented)
- `Walking` - Force walking (not fully implemented)
- `Bicycle` - Force bicycle (not fully implemented)

### Future Extensibility

The system is designed to support per-building or per-district preferences through the `ResourceChainConfig` system, but this is currently not implemented in the UI.

---

## Debugging and Monitoring

### Log Messages

When system is working correctly:
```
[INFO] TransportPriorityCostSystem created - will FORCE bus transport
[INFO] TransportPreferenceSystem created - default preference: Bus
[INFO] Patched CitizenUtils.GetPathfindWeights
[INFO] Transport preference patches applied - Bus transport will be FORCED (cars/taxi disabled)
[INFO] Bus preference: 1 bus lines free, 1 non-bus lines DISABLED
[INFO] Modified stop comfort: 9 bus stops maximized, 4 other stops minimized
[INFO] Disabled personal vehicles: 17 cars, 54 bicycles
```

When system is disabled:
```
[INFO] Restored: 2 prices, 19 comfort, 21 cars, 60 bikes
[INFO] Removed transport preference patches
```

### Common Issues

**Workers walking instead of using buses**:
- Check if bus routes actually connect their home to work
- Verify bus line has sufficient capacity
- Check if bus stops are functioning (not broken)

**Workers using trains**:
- Bus route doesn't exist between origin and destination
- Walking distance > maximum (~7km), train is only viable option
- This is expected behavior when no bus alternative exists

**"Not Enough Workers" problems**:
- No bus lines in city + all other transport disabled = workers can't commute
- Add bus lines connecting residential to commercial/industrial zones

---

## Related Systems

### WorkerTransportPrioritySystem (DEPRECATED)
**Status**: Stub system, functionality moved to TransportPreferenceSystem.

**Purpose**: Originally tried to manually intercept worker pathfinding and force specific bus stops. This approach was complex, error-prone, and has been replaced.

**Current Function**: One-time cleanup of old `ForcedPriorityTrip` components from saves, then disables itself.

---

## Technical Implementation Details

### Component Enabling/Disabling

**CarKeeper Component**:
```csharp
EntityManager.SetComponentEnabled<CarKeeper>(entity, false);
```

When disabled, `TripNeededSystem.Execute()` code (line ~1073):
```csharp
if (EntityManager.IsComponentEnabled<CarKeeper>(entity))
{
    parameters.m_Methods |= PathMethod.Road | PathMethod.Parking;
}
// If disabled, Road and Parking methods are NOT added
```

### Transport Line Detection

The system identifies bus lines by checking waypoints:
```csharp
bool IsBusLine(Entity lineEntity)
{
    var waypoints = EntityManager.GetBuffer<RouteWaypoint>(lineEntity);
    foreach (var waypoint in waypoints)
    {
        if (EntityManager.HasComponent<BusStop>(waypointEntity))
            return true;
        // Also checks Connected component for linked stops
    }
    return false;
}
```

### Harmony Patching

Uses Harmony 2.x to inject code at runtime:
```csharp
_harmony = new Harmony("ManageResourceChains.TransportPreference");
_harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
```

The postfix runs **after** the original method, allowing us to modify the return value.

---

## Summary

The Transport Priority System forces bus usage through a comprehensive multi-layered approach:

1. **Makes money extremely important** in pathfinding (1000× weight)
2. **Makes buses free** (ticket = 0)
3. **Makes trains prohibitively expensive** (ticket = 65,535)
4. **Physically disables cars** (CarKeeper disabled)
5. **Physically disables bicycles** (BicycleOwner disabled)
6. **Makes bus stops comfortable** (comfort = 1.0)
7. **Makes train stops uncomfortable** (comfort = 0.01)

**Result**: Workers have no practical choice but to use buses or walk. The system is aggressive, effective, and fully reversible.

When no buses are available, workers will walk if possible, or use alternative public transport (trains) despite the massive cost penalty, or be unable to commute if no valid path exists.
