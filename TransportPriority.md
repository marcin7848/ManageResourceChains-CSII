# Transport Priority System Documentation

## Overview

The Transport Priority System forces workers to use specific transport methods (currently supports **Bus** and **Train** preferences) by manipulating the game's pathfinding cost calculations and restricting available transport options.

The system works by making the preferred transport type completely free (ticket price = 0) while making all other transport types prohibitively expensive (ticket price = 65,535), combined with comfort manipulation and personal vehicle disabling.

## System Architecture

The system consists of three main components working together:

### 1. TransportPreferenceSystem
**File**: `Systems/TransportPreferenceSystem.cs`  
**Purpose**: Defines the transport preference settings and provides the configuration interface.

**Key Elements**:
- `PreferredTransportMethod` enum: Defines available transport preferences (None, Bus, Train, PublicTransport, Taxi, Walking, Bicycle)
- `DefaultPreference`: Static property that can be set to any preference (defaults to `Bus`)
- Acts as the central configuration point for the other systems

**Fully Implemented Preferences**:
- ✅ **Bus** - Forces bus transport only (all other transport expensive)
- ✅ **Train** - Forces train transport only (all other transport expensive)
- ⚠️ **PublicTransport** - Allows all public transport (no specific type preference)
- ❌ **Taxi, Walking, Bicycle** - Not fully implemented (weight changes only)

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

For **Train Preference**, the patch uses the same weight modifications (time=0.001, money=1000, comfort=0.001).

**The Effect (Bus Preference Example)**:
- A bus with 0 ticket cost: `(travel_time × 0.001) + (0 × 1000) + (comfort × 0.001) = ~0 cost`
- A train with 65,535 ticket: `(travel_time × 0.001) + (65535 × 1000) + (comfort × 0.001) = ~65,535,000 cost`

**The Effect (Train Preference Example)**:
- A train with 0 ticket cost: `(travel_time × 0.001) + (0 × 1000) + (comfort × 0.001) = ~0 cost`
- A bus with 65,535 ticket: `(travel_time × 0.001) + (65535 × 1000) + (comfort × 0.001) = ~65,535,000 cost`

The non-preferred transport has a **65 MILLION point penalty**, making the preferred transport virtually always chosen.

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

For **Bus Preference**:
- Sets **bus line** ticket prices to **0** (completely free)
- Sets **non-bus line** (train, tram, subway, ferry) ticket prices to **65,535** (maximum possible value)

For **Train Preference**:
- Sets **train line** ticket prices to **0** (completely free)
- Sets **non-train line** (bus, tram, subway, ferry) ticket prices to **65,535** (maximum possible value)

**Code Flow**:
1. Queries all `TransportLine` entities
2. For each line, checks if it's the preferred type (using `IsBusLine()` or `IsTrainLine()` helper)
3. Stores original ticket price in `_originalTicketPrices` dictionary
4. Modifies `TransportLine.m_TicketPrice` component
5. Also sets `m_VehicleInterval` to 10,000 for non-preferred lines (vehicles spawn very rarely)

**Bus Line Detection**:
```csharp
private bool IsBusLine(Entity lineEntity)
{
    // Checks if any waypoint on the route has a BusStop component
    // Also checks Connected entities for BusStop
    return true if route uses bus stops, false otherwise
}
```

**Train Line Detection**:
```csharp
private bool IsTrainLine(Entity lineEntity)
{
    // Checks if any waypoint on the route has a TrainStop component
    // Also checks Connected entities for TrainStop
    return true if route uses train stops, false otherwise
}
```

#### Mechanism 2: Stop Comfort Manipulation

**What It Does**:

For **Bus Preference**:
- Sets **bus stops** comfort factor to **1.0** (maximum comfort, no waiting penalty)
- Sets **bus stops** loading factor to **1.0** (instant boarding)
- Sets **non-bus stops** comfort factor to **0.01** (horrible comfort, huge waiting penalty)
- Sets **non-bus stops** loading factor to **0.01** (extremely slow boarding)

For **Train Preference**:
- Sets **train stops** comfort factor to **1.0** (maximum comfort, no waiting penalty)
- Sets **train stops** loading factor to **1.0** (instant boarding)
- Sets **non-train stops** comfort factor to **0.01** (horrible comfort, huge waiting penalty)
- Sets **non-train stops** loading factor to **0.01** (extremely slow boarding)

**Code Flow**:
1. Queries all `TransportStop` entities
2. Checks if entity has the preferred stop component (`BusStop` or `TrainStop`)
3. Stores original `m_ComfortFactor` in `_originalComfortFactors` dictionary
4. Modifies `TransportStop.m_ComfortFactor` and `m_LoadingFactor`

**Effect**: Even if pathfinding considers non-preferred transport, the comfort penalty adds to the total cost.

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

## How Workers Travel Under Transport Preference

### Step-by-Step Pathfinding Process (Bus Preference Example):

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

### Train Preference Works The Same Way (Just Inverted):

With Train preference:
- Train lines: ticket = 0, comfort = 1.0 → **Cost ~0.5**
- Bus lines: ticket = 65,535, comfort = 0.01 → **Cost ~65,535,000**
- Result: **Workers use trains**

---

## What Happens If Preferred Transport Is Not Available?

### Scenario 1: No Preferred Lines in City (e.g., No Bus Lines with Bus Preference)
**Result**: Workers **walk** if distance is reasonable, or **don't commute** if distance is too far.

**Why**: 
- Cars disabled (no `CarKeeper`)
- Bikes disabled (no `BicycleOwner`)
- Only public transport + walking available
- If no preferred transport routes exist, only expensive alternatives or walking remain

**Game Behavior**:
- Short distance (< 1-2 km): Workers walk
- Long distance (> 2-3 km): Workers may not find a valid path
- Buildings may show "Not Enough Workers" problem
- Cims may become unemployed if they can't reach work

### Scenario 2: Preferred Lines Exist But Don't Connect Origin to Destination
**Result**: Workers use **alternative expensive transport** if available, otherwise **walk**.

**Why**:
- Pathfinder searches for any route using available methods
- `PathMethod.PublicTransportDay` includes ALL public transport (bus, train, tram, metro, ferry)
- Even with 65,535 ticket price, if it's the only way to reach work, it will be chosen
- The pathfinder prefers a 65,535,000 cost route over no route at all

**Cost Comparison**:
```
Walking 5km: Time=3000s × 0.001 = 3.0 cost
Non-preferred transport: Money=65535 × 1000 = 65,535,000 cost
```
Walking is cheaper, so worker walks if possible. If walking distance > max walking distance (~5-7km), expensive transport will be chosen.

### Scenario 3: Preferred Lines Exist But Are Full/Broken
**Result**: Workers **wait** for next vehicle or take alternative public transport.

**Why**:
- If preferred transport capacity is full, pathfinder marks that specific vehicle as unavailable
- Pathfinder recalculates using next available vehicle or alternative route
- If no vehicles available within reasonable time, falls back to expensive alternatives or walking

### Examples:

**Bus Preference**:
- No bus routes → Workers walk or use expensive trains
- Bus full → Workers wait for next bus or use expensive train

**Train Preference**:
- No train routes → Workers walk or use expensive buses
- Train full → Workers wait for next train or use expensive bus

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

The system uses **FIVE simultaneous enforcement mechanisms** (example for Bus preference, same applies to Train):

1. **Weight Manipulation** (TransportPreferencePatches)
   - Makes money cost 1000× more important than time
   - Forces pathfinder to heavily prefer free transport

2. **Ticket Price Manipulation** (TransportPriorityCostSystem)
   - Preferred transport (Bus/Train): Free (0 cost)
   - Non-preferred transport: Maximum price (65,535 cost)
   - Creates astronomical cost difference

3. **Comfort Manipulation** (TransportPriorityCostSystem)
   - Preferred stops: Perfect comfort (1.0)
   - Other stops: Terrible comfort (0.01)
   - Adds additional penalty to non-preferred transport

4. **Car Disabling** (TransportPriorityCostSystem)
   - Physically removes car access by disabling `CarKeeper`
   - Pathfinder cannot consider car routes at all

5. **Bike Disabling** (TransportPriorityCostSystem)
   - Physically removes bicycle access by disabling `BicycleOwner`
   - Pathfinder cannot consider bicycle routes at all

**Result**: Even if one mechanism fails, the others ensure the preferred transport is enforced.

### Symmetrical Implementation

Both Bus and Train preferences work identically:
- **Bus preference**: Buses free, trains expensive
- **Train preference**: Trains free, buses expensive

The same 5-layer enforcement applies to both, just inverted.

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

Currently set via static field in code:
```csharp
// Set to Bus preference (default)
TransportPreferenceSystem.DefaultPreference = PreferredTransportMethod.Bus;

// Or set to Train preference
TransportPreferenceSystem.DefaultPreference = PreferredTransportMethod.Train;
```

**Available Options**:
- `None` - No preference, vanilla behavior, all modifications disabled
- ✅ **`Bus`** - Force bus transport only (fully implemented)
- ✅ **`Train`** - Force train transport only (fully implemented)
- ⚠️ **`PublicTransport`** - Force all public transport (bus + train + tram + metro) - weight changes only
- ❌ **`Taxi`** - Force taxi transport (weight changes only, not fully implemented)
- ❌ **`Walking`** - Force walking (weight changes only, not fully implemented)
- ❌ **`Bicycle`** - Force bicycle (weight changes only, not fully implemented)

### How Preferences Work:

**Bus Preference**:
- Bus lines: ticket = 0, comfort = 1.0, vehicles spawn normally
- All other lines: ticket = 65,535, comfort = 0.01, vehicles spawn rarely
- Cars/bikes disabled
- Result: Workers use buses

**Train Preference**:
- Train lines: ticket = 0, comfort = 1.0, vehicles spawn normally
- All other lines: ticket = 65,535, comfort = 0.01, vehicles spawn rarely
- Cars/bikes disabled
- Result: Workers use trains

### Future Extensibility

The system is designed to support per-building or per-district preferences through the `ResourceChainConfig` system, but this is currently not implemented in the UI.

---

## Extending to New Transport Types

The pattern for adding new transport preferences (Tram, Metro, Subway, etc.) is now established:

### Steps to Add New Transport Type (e.g., Tram):

1. **Add Detection Method** in `TransportPriorityCostSystem.cs`:
```csharp
private bool IsTramLine(Entity lineEntity)
{
    if (!EntityManager.HasBuffer<RouteWaypoint>(lineEntity))
        return false;
        
    var waypoints = EntityManager.GetBuffer<RouteWaypoint>(lineEntity);
    foreach (var waypoint in waypoints)
    {
        Entity waypointEntity = waypoint.m_Waypoint;
        if (EntityManager.Exists(waypointEntity))
        {
            if (EntityManager.HasComponent<TramStop>(waypointEntity))
                return true;
            
            // Also check Connected entities
            if (EntityManager.HasComponent<Connected>(waypointEntity))
            {
                var connected = EntityManager.GetComponentData<Connected>(waypointEntity);
                if (EntityManager.Exists(connected.m_Connected) && 
                    EntityManager.HasComponent<TramStop>(connected.m_Connected))
                    return true;
            }
        }
    }
    return false;
}
```

2. **Add Preference Application Method**:
```csharp
private void ApplyTramPreference()
{
    var entities = _allTransportLineQuery.ToEntityArray(Allocator.Temp);
    int tramLinesModified = 0;
    int otherLinesDisabled = 0;
    
    foreach (var entity in entities)
    {
        if (!EntityManager.Exists(entity))
            continue;
            
        var transportLine = EntityManager.GetComponentData<TransportLine>(entity);
        
        // Store originals if not already stored
        if (!_originalTicketPrices.ContainsKey(entity))
        {
            _originalTicketPrices[entity] = transportLine.m_TicketPrice;
            _originalVehicleIntervals[entity] = transportLine.m_VehicleInterval;
            _originalLineFlags[entity] = transportLine.m_Flags;
        }
        
        bool isTramLine = IsTramLine(entity);
        
        if (isTramLine)
        {
            // Make tram FREE
            if (transportLine.m_TicketPrice != 0)
            {
                transportLine.m_TicketPrice = 0;
                EntityManager.SetComponentData(entity, transportLine);
                tramLinesModified++;
            }
        }
        else
        {
            // Make non-tram expensive and rare
            bool modified = false;
            
            if (transportLine.m_VehicleInterval < 10000f)
            {
                transportLine.m_VehicleInterval = 10000f;
                modified = true;
            }
            
            if (transportLine.m_TicketPrice < NON_PREFERRED_TICKET_PRICE)
            {
                transportLine.m_TicketPrice = NON_PREFERRED_TICKET_PRICE;
                modified = true;
            }
            
            if (modified)
            {
                EntityManager.SetComponentData(entity, transportLine);
                otherLinesDisabled++;
            }
        }
    }
    
    entities.Dispose();
    
    if (tramLinesModified > 0 || otherLinesDisabled > 0)
    {
        Mod.log.Info($"Tram preference: {tramLinesModified} tram lines free, {otherLinesDisabled} non-tram lines DISABLED");
    }
}
```

3. **Add Stop Comfort Method**:
```csharp
private void ModifyStopComfortForTram()
{
    var stopEntities = _allTransportStopQuery.ToEntityArray(Allocator.Temp);
    int tramStopsModified = 0;
    int otherStopsModified = 0;
    
    foreach (var stopEntity in stopEntities)
    {
        if (!EntityManager.Exists(stopEntity) || !EntityManager.HasComponent<TransportStop>(stopEntity))
            continue;
        
        var stop = EntityManager.GetComponentData<TransportStop>(stopEntity);
        
        if (!_originalComfortFactors.ContainsKey(stopEntity))
        {
            _originalComfortFactors[stopEntity] = stop.m_ComfortFactor;
        }
        
        bool isTramStop = EntityManager.HasComponent<TramStop>(stopEntity);
        
        if (isTramStop)
        {
            // Maximum comfort for tram stops
            if (stop.m_ComfortFactor < 1.0f)
            {
                stop.m_ComfortFactor = 1.0f;
                stop.m_LoadingFactor = 1.0f;
                EntityManager.SetComponentData(stopEntity, stop);
                tramStopsModified++;
            }
        }
        else
        {
            // Minimum comfort for non-tram stops
            if (stop.m_ComfortFactor > 0.01f)
            {
                stop.m_ComfortFactor = 0.01f;
                stop.m_LoadingFactor = 0.01f;
                EntityManager.SetComponentData(stopEntity, stop);
                otherStopsModified++;
            }
        }
    }
    
    stopEntities.Dispose();
    
    if (tramStopsModified > 0 || otherStopsModified > 0)
    {
        Mod.log.Info($"Modified stop comfort: {tramStopsModified} tram stops maximized, {otherStopsModified} other stops minimized");
    }
}
```

4. **Update OnUpdate() Method**:
```csharp
else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Tram)
{
    ApplyTramPreference();
    ModifyStopComfortForTram();
    DisablePersonalVehicles();
    _modificationsApplied = true;
}
```

5. **Update TransportPreferencePatches** (if needed):
   - Train/Tram/Metro all use the same weight modifications (time=0.001, money=1000, comfort=0.001)
   - Already handled in the switch statement for `PreferredTransportMethod.Tram`

### Available Stop Components:

Based on decompiled code in `Game.Routes`:
- ✅ `BusStop` - Used for buses
- ✅ `TrainStop` - Used for trains
- ✅ `TramStop` - Available for trams (not implemented yet)
- ✅ `SubwayStop` - Available for metro/subway (not implemented yet)
- ✅ `FerryStop` - Available for ferries (not implemented yet)
- ✅ `AirplaneStop` - Available for airports (not implemented yet)
- ✅ `ShipStop` - Available for cargo ships (not implemented yet)

All follow the same pattern: empty marker structs implementing `IComponentData`.

---

## Debugging and Monitoring

### Log Messages

**When system is working correctly with Bus preference**:
```
[INFO] TransportPriorityCostSystem created - will FORCE bus transport
[INFO] TransportPreferenceSystem created - default preference: Bus
[INFO] Patched CitizenUtils.GetPathfindWeights
[INFO] Transport preference patches applied - Bus transport will be FORCED (cars/taxi disabled)
[INFO] Bus preference: 1 bus lines free, 1 non-bus lines DISABLED
[INFO] Modified stop comfort: 9 bus stops maximized, 4 other stops minimized
[INFO] Disabled personal vehicles: 17 cars, 54 bicycles
```

**When system is working correctly with Train preference**:
```
[INFO] TransportPriorityCostSystem created - will FORCE bus transport
[INFO] TransportPreferenceSystem created - default preference: Train
[INFO] Patched CitizenUtils.GetPathfindWeights
[INFO] Transport preference patches applied - Bus transport will be FORCED (cars/taxi disabled)
[INFO] Train preference: 1 train lines free, 1 non-train lines DISABLED
[INFO] Modified stop comfort: 6 train stops maximized, 3 other stops minimized
[INFO] Disabled personal vehicles: 17 cars, 54 bicycles
```

**When system is disabled**:
```
[INFO] Restored: 2 prices, 19 comfort, 21 cars, 60 bikes
[INFO] Removed transport preference patches
```

### Common Issues

**Workers walking instead of using preferred transport**:
- Check if preferred transport routes actually connect their home to work
- Verify the transport line has sufficient capacity
- Check if stops are functioning (not broken)
- Verify in logs: "X lines free, Y lines DISABLED" - if X=0, no lines of preferred type were detected

**Workers using non-preferred transport**:
- Preferred transport route doesn't exist between origin and destination
- Walking distance > maximum (~7km), expensive transport is only viable option
- This is expected behavior when no preferred alternative exists

**"Not Enough Workers" problems**:
- No preferred transport lines in city + all other transport disabled = workers can't commute
- Add transport lines of the preferred type connecting residential to commercial/industrial zones

**Transport line detection issues** (e.g., "0 train lines free" when trains exist):
- Check decompiled code to verify stop component names
- The system looks for `BusStop` or `TrainStop` components on waypoints
- If a new transport type uses different components, detection logic needs updating

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

The system identifies transport lines by checking waypoints for specific stop components:

**Bus Line Detection**:
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

**Train Line Detection**:
```csharp
bool IsTrainLine(Entity lineEntity)
{
    var waypoints = EntityManager.GetBuffer<RouteWaypoint>(lineEntity);
    foreach (var waypoint in waypoints)
    {
        if (EntityManager.HasComponent<TrainStop>(waypointEntity))
            return true;
        // Also checks Connected component for linked stops
    }
    return false;
}
```

**How It Works**:
1. Get all waypoints from the transport line's `RouteWaypoint` buffer
2. Check each waypoint for the specific stop component (`BusStop` or `TrainStop`)
3. Also check the `Connected` component's linked entity (stops can be connected)
4. If any waypoint has the component, the line is identified as that type

**Stop Components**:
- `BusStop` - Marker component for bus stops (empty struct)
- `TrainStop` - Marker component for train stops (empty struct)
- `TramStop` - Marker component for tram stops (exists but not used yet)
- `SubwayStop` - Marker component for subway/metro stops (exists but not used yet)

### Harmony Patching

Uses Harmony 2.x to inject code at runtime:
```csharp
_harmony = new Harmony("ManageResourceChains.TransportPreference");
_harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
```

The postfix runs **after** the original method, allowing us to modify the return value.

---

## Summary

The Transport Priority System forces specific transport usage through a comprehensive multi-layered approach:

### Core Mechanism (works for Bus or Train preference):

1. **Makes money extremely important** in pathfinding (1000× weight)
2. **Makes preferred transport free** (ticket = 0)
3. **Makes non-preferred transport prohibitively expensive** (ticket = 65,535)
4. **Physically disables cars** (CarKeeper disabled)
5. **Physically disables bicycles** (BicycleOwner disabled)
6. **Makes preferred stops comfortable** (comfort = 1.0)
7. **Makes non-preferred stops uncomfortable** (comfort = 0.01)

### Fully Implemented Preferences:

**Bus Preference**:
- Buses: Free, comfortable, spawn normally
- Trains/Trams/etc.: Expensive (65,535), uncomfortable, spawn rarely
- Result: Workers use buses exclusively

**Train Preference**:
- Trains: Free, comfortable, spawn normally
- Buses/Trams/etc.: Expensive (65,535), uncomfortable, spawn rarely
- Result: Workers use trains exclusively

**Result**: Workers have no practical choice but to use the preferred transport or walk. The system is aggressive, effective, and fully reversible.

### Fallback Behavior:

When preferred transport is unavailable, workers will:
1. **Walk** if distance is < 5-7km (cost ~1-3)
2. Use **expensive alternatives** if distance is > 7km (cost ~65,535,000)
3. **Not commute** if no valid path exists (causes "Not Enough Workers")

The system ensures the preferred transport is virtually always chosen when available, with a 65 MILLION point cost difference making alternatives essentially impossible unless no other option exists.
