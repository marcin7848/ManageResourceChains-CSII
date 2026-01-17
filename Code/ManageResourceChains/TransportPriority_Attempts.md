# Transport Priority Implementation Attempts

This document outlines the various attempts and mechanisms used to force workers in Cities: Skylines II to use specific transport stops and lines as part of the Manage Resource Chains mod.

## Goal
To allow players to define "Transport Priorities" for resource chain rules. When a worker commutes between home and work, they should be forced to use the specified transport stops/lines, even if the game's native pathfinding would prefer a different route (e.g., driving or walking).

---

## Mechanism 1: Soft Forcing (Cost Modification)
**Located in:** `TransportPriorityCostSystem.cs`

### Approach:
Modify the global costs of transport stops and lines to make them universally more attractive to the game's pathfinder.
- **Stop Comfort**: Increased the `m_ComfortFactor` of prioritized stops to the maximum.
- **Stop Loading**: Increased the `m_LoadingFactor` to prioritize boarding at these stops.
- **Ticket Price**: Identified the `TransportLine` associated with prioritized stops and set its `m_TicketPrice` to 0.

### Effects:
- **Pros**: Perfectly natural behavior. Workers walk to stops, wait, and board using native animations. No "teleporting" or "stacking".
- **Cons**: Not guaranteed. Since it affects global costs, the pathfinder still performs a cost-benefit analysis. If a worker has a car and the destination is close, they might still choose to drive if the "cost" of walking to the bus stop is still higher than driving.

---

## Mechanism 2: Hard Forcing (Multi-Stage Trips)
**Located in:** `WorkerTransportPrioritySystem.cs`

### Approach:
Directly intercept the worker's commute trip and break it into multiple stages by hijacking the `Target` and `PathOwner` components.

### Stages:
1. **Intercept**: Detect a new commute trip (`Purpose.GoingToWork`) and check if a matching rule with transport priorities exists.
2. **Redirect to Stop**: Set the worker's `Target.m_Target` to the first prioritized stop entity.
3. **Wait at Stop**: When the worker reaches the stop, switch to a "Waiting" state.
4. **Board Transport**: Ensure the worker boards the correct transport line.
5. **Next Stage**: Once boarded, update the `Target` to the next prioritized stop or the final workplace.

### Issues and Solutions:

#### 1. Immediate Unspawning
- **Cause**: When a worker reached the prioritized stop, the native `ResidentAISystem` detected they had reached their `Target` and initiated the "arrival" sequence, which often resulted in the citizen unspawning.
- **Fix**: Clear arrival flags (`EndReached`, `Arrived`, `Hangaround`) and transition the worker to a custom `WaitingAtPriority` state before the native AI could process the arrival.

#### 2. Refusal to Wait/Board
- **Cause**: Even at the stop, the native AI would see that the "real" target (workplace) was far away and the current path didn't include transport, so it would decide to start walking.
- **Fix**: 
    - Manually inject a 2-stop path into the `PathElement` buffer: `[Current Stop -> Next Stop]`.
    - Set `ResidentFlags.WaitingTransport` and `ResidentFlags.CannotIgnore`.
    - Manually signal the approaching vehicle to stop using `PublicTransportFlags.RequireStop`.

#### 3. Stacking and Teleporting
- **Cause**: Overriding the `m_Lane`, `m_QueueEntity`, and `m_CurvePosition` every frame prevented the native `ResidentAISystem` from calculating natural standing positions and boarding animations.
- **Fix**: Moved to a "one-time setup" when reaching the stop, allowing the native AI's `SetQueuePosition` to handle the actual queuing. Preferred `AccessLane` (platforms) over `Waypoint` (road markers) for waiting.

#### 4. Stacking and De-boarding
- **Cause**: Fixed `m_CurvePosition.y` caused stacking. De-boarding was caused by native AI re-pathfinding and choosing walking/car over the prioritized transport, or thinking it had already reached the target due to stale lane/flag data.
- **Fix**: 
    - Randomize `m_CurvePosition.y` based on entity index to spread passengers naturally along the platform.
    - Maintain `ResidentFlags.CannotIgnore` while in vehicle to prevent the native AI from deciding to walk.
    - Explicitly set `humanLane.m_Lane` to the vehicle entity upon successful boarding to ensure state consistency with the native `ResidentAISystem`.

---

## Current State & Remaining Challenges
The "Soft Forcing" works naturally but isn't strict. The "Hard Forcing" is now more stable by better synchronizing with the game's native AI states during the delicate boarding-to-travel transition.

### Latest Fix (January 2026) - Addressing Stacking and Early De-boarding

#### Issue 1: Passengers stacking at bus stops
**Root Cause**: Setting `CreatureLaneFlags.WaitPosition` and manually setting `m_CurvePosition.y` interfered with the native AI's queue positioning system. The native `SetQueuePosition` function uses the entity's actual transform position to calculate queue areas via `CreatureUtils.GetQueueArea()`, not the curve position we were setting.

**Fix**: 
- Removed `CreatureLaneFlags.WaitPosition` flag
- Removed manual `m_CurvePosition` overrides
- Let the native AI's `TransportStopReached` and `SetQueuePosition` handle natural queue distribution

#### Issue 2: Passengers leaving bus early (before destination)
**Root Cause**: The native AI's `CurrentVehicleBoarding` function checks if a passenger should exit by examining:
```csharp
// Check at path[elementIndex + 1]
Entity target2 = pathElements[pathOwner.m_ElementIndex + 1].m_Target;  // Destination waypoint
Entity nextLane = pathElements[pathOwner.m_ElementIndex + 2].m_Target; // For pathfinding after exit
if (RouteUtils.ShouldExitVehicle(nextLane, target2, controllerVehicle, ...))
{
    pathOwner.m_ElementIndex += 2;  // Skip past transport segment
    resident.m_Flags |= ResidentFlags.Disembarking;
}
```

The `ShouldExitVehicle` function returns true when:
- `connectedData[targetWaypoint].m_Connected` has a `BoardingVehicle` component
- `boardingVehicleData[connected].m_Vehicle == currentVehicle` (bus is currently at this stop)

**Root Problems:**
1. Using `FindNextWaypointOnLine(boardingWaypoint)` returned the NEXT stop on the line, not the actual destination stop near the target building. The bus would exit at the very next stop!
2. Path was incorrectly structured - we were adding the vehicle to the path, but the native AI expects waypoints only.

**Fix**:
- Created `FindDestinationWaypointOnLine(boardingWaypoint, target)` that finds the waypoint on the bus line that is CLOSEST to the target building (within 500m)
- Path structure while on vehicle MUST be: `[boarding_waypoint, destination_waypoint, final_target]`
  - `[0]` = boarding waypoint (where we got on)
  - `[1]` = destination waypoint (where to get off) - checked by `ShouldExitVehicle`
  - `[2]` = final target (where to walk after exiting) - after `elementIndex += 2`
- DO NOT add vehicle entity to path - the native AI doesn't expect it there!

#### Issue 3: Broken pathfinding after exiting bus
**Root Cause**: After exiting, `pathOwner.m_ElementIndex += 2` advances the index to point at element `[2]`. The native pathfinder creates COMPLETE paths with all lane information (pedestrian paths, crosswalks, etc), then `RouteUtils.StripTransportSegments` removes the middle portion representing the vehicle ride, leaving: `[path_to_stop, boarding_waypoint, exit_waypoint, path_from_stop]`.

Our manual path `[boarding_waypoint, exit_waypoint, final_target]` only had waypoint entities without lane information. When `elementIndex` advanced to `[2]`, the passenger tried to walk directly to the building entity with no path guidance, causing them to walk across roads randomly.

**Fix**: 
- **DON'T add the final target to the path at all**
- Use only `[boarding_waypoint, exit_waypoint]` as the path
- When `ShouldExitVehicle` is called with a 2-element path:
  - `nextLane = path[elementIndex + 2]` will be `Entity.Null` (out of bounds)
  - This causes `obsolete = false` by default in `ShouldExitVehicle`
  - BUT if the route doesn't match, it will set `obsolete = true` anyway
- When path is marked `Obsolete`, the native AI will request a PROPER pathfinding with all lane information from the exit stop to `target.m_Target`
- After exit handling in `MovingToFinalTarget` state: ensure `target.m_Target` is correctly set to final destination and call `EndForcedTrip` to let native pathfinding take over

**Key Learning**: Never manually add non-lane entities to paths - the native pathfinder must create complete paths with all lane segments. For multi-stop transport trips, provide only waypoints and let the path be marked Obsolete for re-pathfinding after each leg.

**Key Learning**: The native AI's path structure for public transport:
- **When walking to stop**: `TransportStopReached` uses `path[elementIndex]` as current waypoint and `path[elementIndex + 1]` as next waypoint
- **When in vehicle**: `CurrentVehicleBoarding` uses `path[elementIndex + 1]` as exit check and `path[elementIndex + 2]` for post-exit pathfinding
- **After exit**: `elementIndex += 2` to skip transport segment and continue walking


