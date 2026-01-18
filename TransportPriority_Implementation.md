# Transport Priorities Implementation Documentation

## Overview
This document outlines the research and proposed implementation for the "Transport Priorities" feature in the Manage Resource Chains mod. The goal is to allow players to select specific transport lines, stops, or stations and prioritize their use for workers, resources, or services.

## 1. Selectable Transport Entities

The Cities: Skylines II engine represents transport infrastructure through several entity types. For this mod, we can support picking:

| Entity Type | Game Component | Description |
| :--- | :--- | :--- |
| **Transport Line** | `Game.Routes.TransportLine` | An entire route (e.g., "Bus Line 1"). Picking this prioritizes the whole line. |
| **Transport Stop** | `Game.Routes.TransportStop` | A specific point where vehicles stop. Best for granular control. |
| **Transport Station** | `Game.Prefabs.TransportStation` | A building (e.g., Central Train Station) that contains one or more stops. |
| **Cargo Terminal** | `Game.Prefabs.CargoTransportStation` | Specialized buildings for resource transport (Cargo Trains, Ships, etc.). |
| **Taxi Stand** | `Game.Routes.TaxiStand` | Specific locations for taxi pickup/drop-off. |

### Recommended Picking Strategy
- **Stations/Terminals**: Players pick the building. The mod then finds all stops associated with that building (via `Owner` or `Attachment` components).
- **Lines**: Players pick a line from the transport overview or by clicking a vehicle/stop belonging to it.
- **Stops**: Players click the stop marker in the world.

## 2. Transport Ways and Groups

### Workers (Passenger Transport)
Workers use transport modes that support passengers.
- **Modes**: Bus, Train, Tram, Subway, Ferry, Airplane, Helicopter, Taxi.
- **Key Components**: `PublicTransportStationData`, `TransportLineData.m_PassengerTransport = true`.

### Resources (Cargo Transport)
Resources use transport modes that support cargo.
- **Modes**: Cargo Train, Cargo Ship, Cargo Airplane.
- **Key Components**: `CargoTransportStationData`, `TransportLineData.m_CargoTransport = true`.

### Services
Services mostly use road networks, but some specific services use specialized transport.
- **Modes**: Roads (default), Helicopter (Police/Fire/Medical).
- **Note**: "Prioritizing" services usually means prioritizing the road segments they use, but for this mod, it could apply to specialized service terminals.

## 3. Pathfinding Influence Mechanisms

To "force" or prioritize a transport way, we must influence the game's pathfinding cost calculation. The pathfinder (A*) uses `PathfindWeights` consisting of:
- **Time**: Travel time + waiting time.
- **Money**: Ticket prices or fees.
- **Comfort**: Penalty for uncomfortable stops/vehicles.
- **Behaviour**: Internal game logic weights.

### How to Decrease Cost (Increase Priority)

#### A. Modify Ticket Price
- Component: `Game.Routes.TransportLine` -> `m_TicketPrice`.
- Logic: Setting this to 0 or a very low value makes the line more attractive to workers/passengers.

#### B. Modify Comfort Factor
- Component: `Game.Routes.TransportStop` -> `m_ComfortFactor` (0.0 to 1.0).
- Logic: Increasing this value reduces the comfort penalty in `PathUtils.GetTransportStopSpecification`. A value of 1.0 removes the penalty entirely.

#### C. Modify Loading Factor
- Component: `Game.Routes.TransportStop` -> `m_LoadingFactor`.
- Logic: Influences `GetStopDuration`. Higher loading factors can slightly reduce stop time.

#### D. Route Modifiers (Policies)
- Component: `Game.Routes.RouteModifier` buffer on the line entity.
- Logic: Can be used to apply `VehicleInterval` modifiers, effectively reducing waiting time.

## 4. Implementation Feasibility

### Global Priority (High Feasibility)
Modifying the components on the transport entities themselves.
- **Pros**: Relatively easy to implement by updating ECS components.
- **Cons**: Affects EVERYONE in the city, not just the specific workers/resources targeted by a `ResourceChainRule`.

### Per-Rule (Targeted) Priority (Low Feasibility)
Making a line cheap only for specific workers from Building A.
- **Pros**: Perfectly matches the mod's "Rule" philosophy.
- **Cons**: Extremely difficult. The pathfinder is global and Burst-compiled. There is no native support for "per-request weights" for specific transport lines.
- **Possible Workaround**: Intercept `CitizenPathfindSetup` and modify global weights for the whole path, but this still doesn't target a specific line unless we can somehow "tag" the line in the pathfinder.

## 5. Proposed Implementation Plan

### Stage 1: UI & Data Structure
- Expand `TransportStationType` to include all modes (Tram, Bus, Cargo, etc.).
- Update `TransportPriority` interface to support either `stationEntity` or `lineEntity`.
- Implement a "Picker" tool that can select Buildings (Stations) and Routes (Lines).

### Stage 2: Global Component Modification
- Create a `TransportPrioritySystem` that monitors `ResourceChainRule` changes.
- When a line/stop is prioritized, update its `m_TicketPrice` and `m_ComfortFactor` in the ECS.
- Revert changes when the priority is removed.

### Stage 3: "Forcing" Logic
- Since "hard forcing" is not natively supported, implement "soft forcing" by setting the cost to the absolute minimum allowed by the engine.
- For resources, prioritize `CargoTransportStation` by increasing its `m_LoadingFactor`.

## 6. Necessary Game DLL Classes to Inspect
- `Game.Pathfind.PathUtils`: For cost calculation logic.
- `Game.Routes.RouteUtils`: For route data handling.
- `Game.Simulation.TransportLineSystem`: For how lines are updated.
- `Game.Prefabs.TransportLinePrefab`: For default values.

### Feasibility Analysis of Forced Transport Routing

Based on my research into the Cities: Skylines II pathfinding and simulation systems, I can confirm that **both scenarios you described are possible to implement**, though they require different technical approaches.

---

### Scenario 1: Forced Routing for Workers
**Goal**: Force workers from Building A to use a specific Tram Station and Bus Stop on their way to their workplace.

#### Implementation Approach: **Multi-Stage Trip State Machine**
The game's pathfinder is "selfish" and always looks for the cheapest path (time/money/comfort). It does not natively support "via" waypoints for citizens. To implement this, we would use a **multi-stage trip system**:

1.  **Trip Interception**: When a worker from Building A starts a commute to their workplace, the mod intercepts the pathfinding request in `CitizenPathfindSetup`.
2.  **Destination Redirection**: Instead of requesting a path directly to the workplace, the mod changes the destination to the **first prioritized stop** (e.g., the Tram Station).
3.  **Intermediate State**: We attach a custom component (e.g., `ForcedTripState`) to the citizen to store their *actual* final destination (the workplace) and the list of remaining "via" points.
4.  **Arrival Detection**: When the worker arrives at the Tram Station, the mod detects this, clears the current trip, and triggers a new `TripNeeded` request to the next point (the Bus Stop) or the final workplace.
5.  **Sequential Forcing**: This process repeats until the worker reaches their workplace.

**Feasibility**: ✅ **High**. It effectively "tricks" the AI into taking multiple short trips that, combined, form the path you want.

---

### Scenario 2: Forced Routing for Resources
**Goal**: Force resources from a producer building to go through a specific Cargo Train Station before reaching the destination building.

#### Implementation Approach: **Storage Transfer Redirection**
Resource transport in CS2 is managed via `StorageTransferRequest` elements.

1.  **Request Interception**: The mod monitors `StorageTransferRequest` buffers in the `ResourcePathfindSetup` system.
2.  **Target Modification**: If a request matches your rule (Producer A -> Consumer B), the mod changes the `m_Target` of the request from Consumer B to **Cargo Train Station C**.
3.  **Cargo Handover**: The resource is delivered to the Cargo Station normally.
4.  **Secondary Dispatch**: Once the resource is at the Cargo Station, the existing rule system of the mod can be used to ensure the Cargo Station prioritizes sending those resources to Consumer B (using an **OUTGOING** rule on the Cargo Station).

**Feasibility**: ✅ **Very High**. The game's economy system is already designed around intermediate storage (warehouses and terminals), so redirecting the "target" of a delivery is a native operation.

---

### Summary Table

| Feature | Scenario 1: Workers | Scenario 2: Resources |
| :--- | :--- | :--- |
| **Possibility** | Yes | Yes |
| **Complexity** | Medium (Requires trip state tracking) | Low (Redirecting transfer targets) |
| **Behavior** | Home → Station → Stop → Work | Producer → Cargo Terminal → Consumer |
| **Native Support** | No (requires mod logic) | Partial (native storage transfers) |

### Conclusion
Both scenarios are definitely possible. The implementation would involve creating a new system that hooks into the **Pathfind Setup** phase to modify the targets of citizens and resources before the actual pathfinding begins. This ensures that the game "thinks" it's just doing a normal trip to the intermediate station you've chosen.

---

## 7. Implementation: Worker Transport Forcing (COMPLETED - January 2026)

### Overview
Successfully implemented a hard-forcing system that makes workers use specific transport stops when commuting to work. The implementation uses a multi-stage trip state machine combined with careful synchronization with the game's native AI systems.

**Supported Transport Types:**
- Bus Stops and Bus Stations
- Train Stops and Train Stations ✅
- Tram Stops and Tram Stations
- Subway Stops and Subway Stations
- Ferry Stops and Ferry Terminals
- Other waypoint-based public transport

All transport types that use the game's native waypoint system are supported. The system works generically without hardcoded checks for specific transport types.

### System Architecture

#### Transport Type Detection
Located in: `Systems/ResourceChainManagementSystem.cs` → `DetermineStationType()`

The system automatically detects the correct transport type when a station is selected:

**For Waypoint/Stop Entities:**
- Checks for specific component markers: `BusStop`, `TrainStop`, `TramStop`, `SubwayStop`, etc.
- Returns the corresponding station type immediately

**For Building Entities (Stations):**
1. **Primary Method - Route Analysis:**
   - Finds all waypoints in the world
   - Checks if any waypoint is connected to a stop owned by the building
   - Reads the `TransportLineData` from the route's prefab
   - Uses the `m_TransportType` enum to determine the station type
   - **Example:** Train line → `TransportType.Train` → `TransportStationType.TrainStation`

2. **Fallback Method - Prefab Data:**
   - Checks `CargoTransportStationData` for cargo terminals
   - Checks `TransportStationData` refuel types:
     - `m_TrainRefuelTypes` → Train Station
     - `m_AircraftRefuelTypes` → Airport
     - `m_WatercraftRefuelTypes` → Port
     - `m_CarRefuelTypes` → Bus Station

This dual-layer detection ensures correct identification even if stations have no active routes yet.

#### WorkerTransportPrioritySystem
Located in: `Systems/WorkerTransportPrioritySystem.cs`

This system manages the entire lifecycle of forced transport trips for workers.

**Key Components:**
- `ForcedPriorityTrip` - Custom ECS component attached to workers to track forced trip state
- Trip states: `MovingToPriority`, `WaitingAtPriority`, `MovingToFinalTarget`
- Update interval: Every 16 frames (~0.27 seconds at 60fps)

### Implementation Stages

#### Stage 1: Trip Interception
When a worker starts going to work:
1. System detects `TravelPurpose.m_Purpose == Purpose.GoingToWork`
2. Checks if there's a matching `ResourceChainRule` with transport priorities
3. If yes, redirects the worker's `Target.m_Target` from workplace to the first priority stop
4. Attaches `ForcedPriorityTrip` component with:
   - `m_FinalTarget` = actual workplace
   - `m_CurrentResolvedWaypoint` = waypoint entity to wait at
   - `m_CurrentResolvedStop` = stop entity where bus arrives
   - `m_State` = `MovingToPriority`

#### Stage 2: Waiting at Stop
When worker reaches the bus stop:
1. Detects `EndReached` flag on `HumanCurrentLane`
2. Transitions to `WaitingAtPriority` state
3. Sets up waiting behavior:
   - `ResidentFlags.WaitingTransport` - marks as waiting for transport
   - `ResidentFlags.CannotIgnore` - prevents AI from deciding to walk instead
   - `CreatureLaneFlags.Transport | EndReached` - proper lane state
4. Resolves the stop entity to find its waypoint and connected routes
5. Worker naturally queues at the stop using native AI positioning

**Critical Implementation Detail:**
- Do NOT set `WaitingPosition` flag or manually override `m_CurvePosition`
- Let the native AI's `SetQueuePosition` handle queue distribution naturally
- This prevents passengers from stacking in one spot

#### Stage 3: Boarding Detection
System detects boarding through multiple signals:
1. `CurrentVehicle` component is added by native AI
2. `BoardingVehicle` component exists on vehicle
3. `ResidentFlags.WaitingTransport` flag still set (entering phase)
4. Transitions from entering to `Ready` when `CreatureVehicleFlags.Ready` is set

When fully boarded (`finishedEntering = true`):
1. Clear waiting flags: `WaitingTransport`, `NoLateDeparture`
2. Keep `CannotIgnore` flag to prevent early exit
3. Set `humanLane.m_Lane` to the vehicle entity
4. Clear arrival flags: `EndReached`, `EndOfPath`

#### Stage 4: Path Setup for Bus Ride
**CRITICAL: 2-Element Path Structure**

The path buffer is set up with EXACTLY 2 elements:
```
[0] = boarding_waypoint (where worker got on)
[1] = destination_waypoint (where to exit)
```

**Why only 2 elements?**
When the native AI's `CurrentVehicleBoarding` decides the passenger should exit:
1. It does `pathOwner.m_ElementIndex += 2` (advances by 2)
2. Then `ExitVehicle` checks: `if (elementIndex < path.Length && !Obsolete)`
3. With 2 elements: `2 < 2` = FALSE
4. `ExitVehicle` uses **fallback behavior** (exits at vehicle position)
5. If we added element [2], `ExitVehicle` would try to navigate to it directly, causing edge-of-map walking!

**Destination Waypoint Selection:**
Uses `FindDestinationWaypointOnLine()` to find the waypoint on the bus line that is:
- On the same route as the boarding waypoint
- Closest to the final target (within 500m)
- If no suitable waypoint found, uses next waypoint on line

#### Stage 5: Riding the Bus
While on the bus:
1. `ResidentFlags.CannotIgnore` prevents passenger from deciding to exit early
2. Native AI's `CurrentVehicleBoarding` checks each stop using `ShouldExitVehicle()`
3. `ShouldExitVehicle` compares current stop's waypoint with `path[elementIndex + 1]`
4. When bus reaches destination waypoint, `Disembarking` flag is set
5. `elementIndex` is incremented by 2 (now equals 2)

#### Stage 6: Exit Detection and Cleanup
**Two-Phase Exit Detection:**

**Phase 1: Disembarking Detection (Still in Vehicle)**
When `Disembarking` flag is set AND `CurrentVehicle` component still exists:
```csharp
// Clear path buffer immediately to force fallback behavior
pathElements.Clear();
pathOwner.m_ElementIndex = 0;
pathOwner.m_State |= PathFlags.Obsolete;
```

**Phase 2: Post-Exit Cleanup (After Leaving Vehicle)**
When `CurrentVehicle` component is removed (passenger is on foot):
```csharp
// 1. Clear path buffer
pathElements.Clear();

// 2. Reset path state
pathOwner.m_ElementIndex = 0;
pathOwner.m_State |= PathFlags.Obsolete;
pathOwner.m_State &= ~(PathFlags.Updated | PathFlags.Pending | PathFlags.Failed);

// 3. CRITICAL: Reset HumanCurrentLane with FindLane flag
humanLane.m_Lane = Entity.Null; // Clear vehicle reference
humanLane.m_Flags = CreatureLaneFlags.FindLane; // ONLY FindLane!
humanLane.m_CurvePosition = default;

// 4. Clear resident flags
resident.m_Flags &= ~(ResidentFlags.Disembarking | ResidentFlags.WaitingTransport | ResidentFlags.CannotIgnore);
```

**Why `CreatureLaneFlags.FindLane` is Critical:**
- Without this flag, the passenger has no valid lane information after exiting
- The native AI would attempt to walk in a straight line to the target
- This caused the "walking to edge of map" bug
- `FindLane` tells the native AI: "Find a valid pedestrian lane before attempting to move"

#### Stage 7: Final Pathfinding
After cleanup, request new pathfinding via `PrepareWalkingSegment()`:
```csharp
// Set target to final destination
target.m_Target = finalTarget;

// Mark path as Obsolete | Updated to trigger pathfinding
pathOwner.m_State |= PathFlags.Obsolete | PathFlags.Updated;
pathOwner.m_ElementIndex = 0;

// Clear path buffer
pathElements.Clear();

// Force lane finding
humanLane.m_Lane = Entity.Null;
humanLane.m_Flags = CreatureLaneFlags.FindLane;

// Clear behavioral flags
resident.m_Flags &= ~(ResidentFlags.Arrived | ResidentFlags.WaitingTransport | ResidentFlags.CannotIgnore | ResidentFlags.Disembarking);
```

The native pathfinding system then creates a proper pedestrian path from the current position to the final destination.

### Key Lessons Learned

#### 1. Path Structure is Critical
- **2-element path only** during transport prevents `ExitVehicle` from using invalid building entities
- After `elementIndex += 2`, condition `2 < 2` forces fallback behavior
- Adding element [2] causes navigation directly to building = edge-of-map walking

#### 2. Lane State After Exit
- Must clear `HumanCurrentLane.m_Lane` to `Entity.Null`
- Must set ONLY `CreatureLaneFlags.FindLane` flag
- Clear all other flags to ensure clean state
- Without `FindLane`, passenger walks in straight line

#### 3. Timing is Everything
- Clear path when `Disembarking` is detected (still in vehicle)
- Additional cleanup when `CurrentVehicle` is removed (on foot)
- Two-phase approach prevents race conditions

#### 4. Natural Queuing
- Let native AI handle queue positioning at stops
- Do NOT set `WaitingPosition` flag manually
- Do NOT override `m_CurvePosition` manually
- Native `SetQueuePosition` distributes passengers naturally

#### 5. Flag Management
- `CannotIgnore` while in vehicle prevents early exit
- Clear `WaitingTransport` after boarding completes
- Clear `Disembarking` after exit cleanup
- Proper flag cleanup prevents state conflicts

### System Performance
- Update interval: 16 frames (~0.27s at 60fps)
- Minimal performance impact due to targeted queries
- Only processes workers with `ForcedPriorityTrip` component
- Native AI handles all animation and physics

### Result
Workers successfully:
1. Walk to specified bus stop
2. Wait naturally in queue
3. Board the bus
4. Ride to destination stop
5. Exit at correct location
6. Walk properly to workplace using pedestrian lanes
7. No stacking, no teleporting, no edge-of-map walking

### Files Modified
- `Systems/WorkerTransportPrioritySystem.cs` - Main implementation
- `Systems/TransportPriorityCostSystem.cs` - Soft forcing (cost modification)
- `Systems/ResourceChainManagementSystem.cs` - Transport type detection
- `Mod.cs` - System registration
- `TransportPriority_Attempts.md` - Development documentation

### Technical Notes
- Uses ECS component queries for performance
- Synchronizes with native `ResidentAISystem` execution
- Respects native AI animation states
- Compatible with all public transport types that use waypoints
- Extensible to multiple stops per trip (sequential forcing)

---

## 8. Train Station Support & Universal Transport Type Detection (January 2026)

### Problem
Train stations were incorrectly identified as "Bus Stop" in the UI and workers were not using them for forced transport routing.

### Root Cause
When clicking on a train station in the game, the raycast was hitting the **waypoint entity** (not the building). The initial implementation only checked for stop component types **after** checking buildings, causing waypoints to fall through to the default "BusStop" type.

### Solution: Multi-Tier Transport Type Detection

The `DetermineStationType()` method now uses a sophisticated detection hierarchy:

#### **Tier 1: Waypoint → Connected Stop Type Check**
When a waypoint entity is selected (most common scenario):
1. Get the waypoint's `Connected` component
2. Retrieve the connected stop entity
3. Check for specific stop type markers on the stop:
   - `TrainStop` → Train Station
   - `BusStop` → Bus Stop
   - `TramStop` → Tram Stop
   - `SubwayStop` → Subway Station
   - `FerryStop` → Ferry Terminal
   - `ShipStop` → Port
   - `AirplaneStop` → Airport
   - `TaxiStand` → Taxi Stand

#### **Tier 2: Stop → Owner Building Check**
If the stop has no type marker:
1. Get the stop's `Owner` component
2. Check if owner is a building
3. Recursively call `DetermineStationTypeFromBuilding()`

#### **Tier 3: Waypoint → Route Transport Type**
If no connected stop found:
1. Get waypoint's `Owner` (the route/line entity)
2. Read the route's `TransportLineData` prefab component
3. Map `m_TransportType` enum to station type:
   ```
   Train → TrainStation
   Bus → BusStop
   Subway → SubwayStation
   Tram → TramStop
   Ferry → FerryTerminal
   Ship → Port
   Airplane → Airport
   Taxi → TaxiStand
   ```

#### **Tier 4: Direct Entity Component Check**
For entities that are stops themselves (not waypoints):
- Check for stop component directly on entity
- Same marker components as Tier 1

#### **Tier 5: Building Analysis**
For building entities (`DetermineStationTypeFromBuilding`):

**5a. Route Analysis (Primary):**
1. Query all waypoints in the world
2. Find waypoints connected to stops owned by this building
3. Read the route's `TransportLineData` 
4. Map transport type to station type

**5b. Prefab Data (Fallback):**
1. Check for `CargoTransportStationData` → Cargo Terminal
2. Check `TransportStationData` refuel types:
   - `m_TrainRefuelTypes` → Train Station
   - `m_AircraftRefuelTypes` → Airport
   - `m_WatercraftRefuelTypes` → Port
   - `m_CarRefuelTypes` → Bus Station

### Universal Transport Support

**All waypoint-based public transport types are now fully supported:**

✅ **Buses**: Bus Stops & Bus Stations  
✅ **Trains**: Train Stops & Train Stations  
✅ **Trams**: Tram Stops & Tram Stations  
✅ **Subways**: Subway Stops & Subway Stations  
✅ **Ferries**: Ferry Stops & Ferry Terminals  
✅ **Ships**: Ship Stops & Ports  
✅ **Airplanes**: Airplane Stops & Airports  
✅ **Taxis**: Taxi Stands  

**The system works identically for all types:**
- Workers walk to the designated stop/station
- Wait for the vehicle
- Board the vehicle
- Ride to the destination stop
- Exit and continue to workplace

No hardcoded transport type checks exist in `WorkerTransportPrioritySystem` - it works generically with any entity that has:
- Waypoint component
- Connected component linking to a stop
- Owner component linking to a route
- Route with `TransportLineData`

### Result
- ✅ Correct identification and labeling of all transport station types in UI
- ✅ Workers can be forced to use any public transport type for commuting
- ✅ System is future-proof for new transport types added to the game
- ✅ Robust detection with multiple fallback layers

### Testing
To verify any transport type:
1. Create a transport line (bus, train, tram, etc.)
2. Add the station/stop to a transport priority rule
3. UI should display correct type (e.g., "Train Station (Entity: XXXXX)")
4. Worker should use that transport type to commute to work

### Implementation Files
- `ResourceChainManagementSystem.DetermineStationType()` - Multi-tier detection logic
- `ResourceChainManagementSystem.DetermineStationTypeFromBuilding()` - Building-specific detection
- `WorkerTransportPrioritySystem` - Generic waypoint handling (no type-specific code)
- `Data/ResourceChainConfig.cs` - `TransportStationType` enum with all types


