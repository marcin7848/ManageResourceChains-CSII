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
**Root Cause**: After exiting, `pathOwner.m_ElementIndex += 2` advances the index to point at element `[2]`. The native pathfinder creates COMPLETE paths with all lane information (pedestrian paths, crosswalks, etc). When we were manually injecting a 2-stop path `[boarding_waypoint, destination_waypoint]` and then clearing it or letting it become obsolete, there was a risk that the worker would attempt to walk to the next destination (another stop or the workplace) without a valid lane-based path. This caused them to walk in straight lines, crossing roads randomly.

**Fix**: 
- **Centralized Walking Initialization**: Implemented `PrepareWalkingSegment(residentEntity, targetEntity)`.
- **Force Re-pathfinding**: This helper sets `PathFlags.Obsolete | PathFlags.Updated`, resets `m_ElementIndex` to 0, and clears the `PathElement` buffer.
- **Find Lane Flag**: Crucially, it also sets the `CreatureLaneFlags.FindLane` flag. This forces the native `ResidentAISystem` to find a valid starting lane (sidewalk/stop) before requesting a new path.
- **State Cleanup**: It also clears stale flags like `EndReached`, `EndOfPath`, `WaitingTransport`, and `Disembarking`.
- **Applied to Transitions**: This helper is now called whenever the worker switches to a walking segment:
  1. At the very start of a forced trip.
  2. After exiting a vehicle at a priority stop to walk to the next stop.
  3. After exiting the final vehicle to walk to the workplace.

### Latest Fix (January 2026) - Preventing Pathfinding Reset Loops
#### Issue: Passengers walking in straight lines (crossing roads randomly) after exiting bus
**Root Cause**: The previous logic for resetting the walking segment (`needsPathReset`) was too aggressive. It triggered a reset every frame if `PathFlags.Obsolete` or `ResidentFlags.Disembarking` was true. Because the game's native pathfinder may take several frames to process a request (during which `Obsolete` might stay true before `Pending` is fully set), the mod was repeatedly clearing the path buffer and requesting updates every single frame. This created an infinite loop where a valid, lane-based path could never be established, forcing the worker into fallback "air" navigation (straight-line walking).

**Fix**:
- **Refined Reset Condition**: Updated `needsPathReset` to explicitly ignore workers if they already have `PathFlags.Pending` or `PathFlags.Updated` set. This ensures that once a path request is made, the mod waits for the native pathfinder to respond before attempting another reset.
- **Improved Path Structure**: Added the final target as a 3rd element in the `PathElement` buffer during the transport leg. This ensures that the native AI's `ShouldExitVehicle` check (which looks at `elementIndex + 2`) has a valid target to inspect, and provides a clear starting point for the subsequent walking pathfinding.
- **Robust Flag Cleanup**: Maintained the deep cleaning of behavioral and arrival flags during the transition to ensure a clean hand-off to the pedestrian AI.

**Key Learning**: When interacting with the game's asynchronous pathfinding system, you must implement a "request and wait" pattern. Repeatedly forcing the `Obsolete` flag or clearing buffers every frame will starve the pathfinder and lead to broken navigation behavior. Always respect the `Pending` and `Updated` states.

**CRITICAL**: The 3-element path structure `[boarding_waypoint, exit_waypoint, final_target]` is REQUIRED because:
- If path only has 2 elements, `CurrentVehicleBoarding` sets `nextLane = Entity.Null` (element [2] doesn't exist)
- `ShouldExitVehicle` checks `if (nextLane != Entity.Null && ...)` - this is SKIPPED when nextLane is null
- Result: `obsolete` stays `false`, path is NOT marked Obsolete
- `elementIndex` advances to 2 (pointing at nothing), passenger has no valid pathfinding guidance
- Passenger walks in straight lines, crossing roads randomly!
- With 3 elements, `nextLane = final_target`, and since its owner differs from the stop's owner, `obsolete = true`
- Path is properly marked Obsolete, and `PrepareWalkingSegment` clears it and requests new proper pathfinding

**Key Learning**: The native AI's path structure for public transport:
- **When walking to stop**: `TransportStopReached` uses `path[elementIndex]` as current waypoint and `path[elementIndex + 1]` as next waypoint
- **When in vehicle**: `CurrentVehicleBoarding` uses `path[elementIndex + 1]` as exit check and `path[elementIndex + 2]` for post-exit pathfinding
- **After exit**: `elementIndex += 2` to skip transport segment and continue walking

### Final Fix (January 2026) - Emergency Path Clearing on Exit

#### Issue: Passengers walking to edge of map and disappearing after exiting bus
**Root Cause**: Even with the emergency detection calling `PrepareWalkingSegment`, there was a race condition. When the native AI's `CurrentVehicleBoarding` does `pathOwner.m_ElementIndex += 2`, the passenger is now pointing at path element [2] (the building entity with NO lane information). Before `PrepareWalkingSegment` could complete its work (clearing path, requesting new pathfinding), the passenger was trying to walk using this invalid path element for 1-2 frames, causing them to walk in a straight line to the edge of the map.

**Fix**: **Immediately clear the path buffer** as soon as we detect `elementIndex >= 2`:
1. Clear the entire path buffer (`pathElements.Clear()`)
2. Reset `elementIndex` to 0
3. Mark path as `Obsolete`
4. Save the pathOwner component
5. **THEN** check if we need to call `PrepareWalkingSegment` (only if not already `Pending` or `Updated`)
6. Skip rest of processing with `continue`

This ensures the invalid path element [2] is removed **before** the passenger can try to use it, preventing the edge-of-map walking behavior completely.

**Key Insight**: The fix must happen in the **correct order**:
1. First: Clear the path (prevent using invalid element)
2. Second: Request new path (only if needed)
3. Third: Continue (skip rest of logic this frame)

This atomic operation prevents any window where the passenger could use the invalid building entity as a navigation target.

### Critical Timing Fix (January 2026) - Detect Disembarking BEFORE ExitVehicle

#### Issue: Path clearing was happening too late
**Root Cause Discovery**: By analyzing the decompiled `ExitVehicle` function in `ResidentAISystem.cs`, I found that:

1. `CurrentVehicleBoarding` sets `Disembarking` flag and does `elementIndex += 2`
2. `ExitVehicle` runs WHILE the passenger still has `CurrentVehicle` component
3. `ExitVehicle` uses `path[elementIndex]` to determine exit target position
4. If `elementIndex=2` and `path[2]` is a building entity, it uses that building's position!
5. THEN the `CurrentVehicle` component is removed
6. Our previous detection (`!hasVehicle && !inVehicle`) was running AFTER all this

The native `ExitVehicle` function code:
```csharp
if (pathOwner.m_ElementIndex < path.Length && (pathOwner.m_State & PathFlags.Obsolete) == 0)
{
    PathElement pathElement = path[pathOwner.m_ElementIndex];
    // Uses pathElement.m_Target to get target position!
    // Calls FixPathStart with this element
}
```

**Fix**: Detect `Disembarking` flag while passenger STILL HAS `CurrentVehicle`:
```csharp
if (isDisembarking && hasVehicle)
{
    // Still have CurrentVehicle but Disembarking flag set
    // Native AI is about to run ExitVehicle - clear path NOW!
    pathElements.Clear();
    pathOwner.m_ElementIndex = 0;
    pathOwner.m_State |= PathFlags.Obsolete;
    EntityManager.SetComponentData(residentEntity, pathOwner);
    continue;
}
```

This ensures the path is cleared BEFORE `ExitVehicle` can use our invalid path elements. When `ExitVehicle` sees an empty path or `PathFlags.Obsolete`, it uses fallback behavior instead of trying to navigate to our building entity.

**Key Learning**: The native AI's exit sequence is:
1. `CurrentVehicleBoarding` - decides to exit, sets `Disembarking`, `elementIndex += 2`
2. `ExitVehicle` - actually exits, uses path (STILL has `CurrentVehicle`!)
3. After exit - `CurrentVehicle` component removed

Detection must happen at step 1-2, not step 3!

### FINAL SOLUTION: 2-Element Path Only (January 2026)

#### Issue: Even with early detection, ExitVehicle still used invalid path element
**Root Cause Discovery**: By analyzing the execution flow more carefully:
1. `CurrentVehicleBoarding` and `ExitVehicle` are called IN THE SAME FRAME, in sequence
2. Our mod system runs in a DIFFERENT phase of the frame, AFTER the native AI has already executed
3. So even if we detect `Disembarking` flag, `ExitVehicle` has ALREADY run and used the invalid path

**The Real Fix**: Don't put the building entity in the path AT ALL!

Instead of a 3-element path `[boarding_waypoint, exit_waypoint, building]`, use only 2 elements: `[boarding_waypoint, exit_waypoint]`.

Here's why this works:
1. `CurrentVehicleBoarding` does `elementIndex += 2`, so `elementIndex = 2`
2. `ExitVehicle` checks: `if (elementIndex < path.Length && !Obsolete)`
3. With 2 elements: `2 < 2` = FALSE
4. `ExitVehicle` uses FALLBACK behavior (vehicle position) instead of trying to navigate to our building!
5. Path is properly marked Obsolete by `CurrentVehicleBoarding`
6. After the native AI is done, our code detects the exit and calls `PrepareWalkingSegment` for proper pathfinding

**Key Insight**: The goal is to make the condition `elementIndex < path.Length` evaluate to FALSE, so `ExitVehicle` never tries to use our path elements as navigation targets.

**Path structure during transport:**
```
[0] = boarding_waypoint (where we got on)
[1] = exit_waypoint (where to get off) - checked by ShouldExitVehicle
DO NOT ADD [2]!
```

This ensures:
- `ShouldExitVehicle` works correctly (only needs elements 0 and 1)
- `ExitVehicle` uses fallback (because elementIndex >= path.Length after +2)
- No building entity in path = no walking to edge of map!




