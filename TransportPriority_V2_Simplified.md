# Simplified Transport Priority System V2

## The Problem with V1

The original implementation tried to:
1. Manually control which transport LINE the worker should board
2. Set up specific waypoints and exit points
3. Handle boarding/exiting logic manually

This was problematic because:
- The game's native AI already picks the BEST transport line automatically
- Manual line control conflicts with the game's pathfinding
- Complex state machine needed to track boarding, riding, exiting
- Multi-line stations were difficult to handle (which line to pick?)

## The V2 Solution: Let the Game Do the Work!

The key insight: **The game's pathfinding system already calculates the optimal multi-modal route**

When you request a path from A to B, the game's pathfinder:
- Considers ALL possible routes (walking, driving, multiple transport lines)
- Calculates costs (time, money, comfort) for each option
- Automatically picks the BEST route including which lines to use
- Creates a complete path with boarding/exit waypoints

### New Simple Approach

Instead of controlling transport lines, we just **force the path to include specific stations as waypoints**.

```
OLD APPROACH (V1):
Home → [Force worker to board Line X at Station A] → [Force exit at Stop Y] → Work
                    ↑ COMPLEX! ↑

NEW APPROACH (V2):
Home → [pathfind to Station A] → Station A → [pathfind to Work] → Work
              ↑ Let game decide!           ↑ Let game decide!
```

### How It Works

1. **Intercept Commute Start**
   - Worker starts `GoingToWork` trip
   - Check if there's a rule with transport priorities
   - If yes, redirect `Target` from workplace to the priority station
   - Let game's pathfinder calculate route to station (it picks the best lines!)

2. **Arrival Detection**
   - Monitor if worker's `CurrentBuilding` matches the station
   - When worker reaches station, we know they've completed the first leg

3. **Continue to Final Destination**
   - Change `Target` from station to actual workplace
   - Mark path as obsolete to trigger re-pathfinding
   - Game calculates best route from station to workplace

4. **Cleanup**
   - When worker reaches workplace, remove the tracking component
   - Journey complete!

### Benefits

✅ **Much simpler code** - No complex boarding/exit state machine  
✅ **Game picks optimal lines** - Native AI knows best routes  
✅ **Multi-line stations work automatically** - Game chooses which line  
✅ **No path manipulation needed** - Just change the target  
✅ **Natural behavior** - Worker moves like any other citizen  
✅ **Future-proof** - Works with any transport type the game supports  

### Code Structure

```csharp
public struct ForcedStationTrip : IComponentData
{
    public Entity m_FinalDestination;      // The actual workplace
    public Entity m_CurrentStationTarget;   // Current station to visit
    public int m_CurrentPriorityIndex;      // Index in the priority list
    public int m_TotalPriorities;           // Total stations to visit
    public ForcedStationTripState m_State;  // GoingToStation or GoingToDestination
}
```

### Processing Flow

```
Frame N: Worker starts GoingToWork
         → Intercept, add ForcedStationTrip
         → Target = Station, State = GoingToStation
         → PathOwner.State |= Obsolete (trigger pathfinding to station)

Frame N+X: Worker arrives at Station
         → Target = Workplace, State = GoingToDestination  
         → PathOwner.State |= Obsolete (trigger pathfinding to workplace)

Frame N+Y: Worker arrives at Workplace
         → Remove ForcedStationTrip component
         → Done!
```

### Comparison

| Aspect | V1 (Complex) | V2 (Simple) |
|--------|--------------|-------------|
| Line selection | Manual | Game AI |
| Boarding logic | Custom | Native |
| Exit logic | Custom | Native |
| Path manipulation | Heavy | Minimal |
| Multi-line support | Difficult | Automatic |
| Code complexity | High | Low |
| State tracking | 6+ states | 2 states |

### Limitations

1. **Arrival detection depends on `CurrentBuilding`** - Worker must physically be IN the station building for detection to work. If they're just passing through a stop, it might not detect.

2. **Multiple stations require additional work** - Currently simplified to support one station waypoint. Full implementation would need to track the rule and iterate through priorities.

3. **Edge cases** - If pathfinding fails to station, worker might get stuck. Need fallback logic.

### Future Enhancements

1. **Multiple station waypoints** - Support forcing path through multiple stations in sequence
2. **Fallback logic** - If station is unreachable, skip and continue to destination
3. **Performance optimization** - Batch processing of workers
4. **UI feedback** - Show which workers are on forced routes

### Testing

1. Select a residential building
2. Add a rule with Worker transport type
3. Add a transport priority (train station, bus stop, etc.)
4. Workers from that building should:
   - Walk/travel to the station first
   - Then continue to their workplace

Monitor logs for:
- "Intercepted worker X: Redirecting from workplace Y to station Z"
- "Worker X reached station Z"
- "Worker X now going to final destination Y"
- "Worker X reached final destination Y"

---

**Date**: January 20, 2026  
**Version**: V2 (Simplified)  
**Status**: Ready for testing
