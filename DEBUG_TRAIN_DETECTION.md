# DEBUG: Train Lines Not Being Detected

## The Problem

Looking at the logs:
```
Train preference: 0 train lines free, 1 non-train lines DISABLED
```

**The system found ZERO train lines!**

This means `IsTrainLine()` is returning `false` for ALL transport lines, including your actual train line. The system is making ALL lines expensive (ticket = 65,535), so workers choose to walk or won't commute at all.

## Why Workers Used Buses

Since ALL lines were marked as expensive (ticket = 65,535), but bus stops are physically closer:
- Walk 100m to bus stop + expensive bus = still shorter total distance
- Walk 300m to train stop + expensive train = longer total distance
- Result: Workers chose bus despite both being expensive

## The Fix Applied

I've added **extensive debug logging** to `IsTrainLine()` that will show:

1. How many waypoints each transport line has
2. What components each waypoint has (TrainStop, BusStop, TramStop, etc.)
3. Whether it finds TrainStop components
4. Whether connected entities have TrainStop

## What To Do Next

### 1. Run the game with the new build

### 2. Watch the log file while the game loads

Look for messages like:
```
Checking line 12345: 4 waypoints
  Waypoint 67890: Train=false, Bus=true, Tram=false, Subway=false, TransportStop=true
  Waypoint 67891: Train=false, Bus=true, Tram=false, Subway=false, TransportStop=true
Line 12345: NOT a train line

Checking line 54321: 6 waypoints
  Waypoint 99999: Train=true, Bus=false, Tram=false, Subway=false, TransportStop=true
  -> Found TrainStop! Line is a train line
```

### 3. Share the log output

The debug output will tell us:
- **Are train lines being checked?** (Should see "Checking line X")
- **Do train stops have TrainStop component?** (Look for Train=true)
- **Are waypoints missing components?** (All false = problem)

## Possible Root Causes

### Theory 1: Train Route Uses Different Component
- Maybe trains use `SubwayStop` instead of `TrainStop`
- Maybe trains use a different marker component
- Debug log will show what components exist

### Theory 2: Route Type Detection Issue
- Maybe the game doesn't use stop component types to identify lines
- Maybe need to check `TransportLine.m_RouteType` or similar
- Need to see what the actual components are

### Theory 3: Connected Entity Issue
- Maybe train stops are connected entities, not direct waypoints
- The Connected logic might not be working
- Debug log will show if Connected entities exist

## Quick Check You Can Do

**In your city**:
1. Open Transport Lines panel
2. Check if you have a train line
3. Note the train line color/name
4. Check if train stops appear on the map

If train line exists in-game but system says "0 train lines", then the detection logic is definitely wrong and we need to fix the `IsTrainLine()` implementation based on what the debug logs reveal.

## Expected Log Output

With the new logging, you should see something like:
```
[INFO] Train preference: checking 2 transport lines
[INFO] Checking line 1234: 4 waypoints
[INFO]   Waypoint 5678: Train=false, Bus=true, Tram=false, Subway=false, TransportStop=true
[INFO]   Waypoint 5679: Train=false, Bus=true, Tram=false, Subway=false, TransportStop=true
[INFO] Line 1234: NOT a train line
[INFO] Checking line 9999: 6 waypoints
[INFO]   Waypoint 11111: Train=true, Bus=false, Tram=false, Subway=false, TransportStop=true
[INFO]   -> Found TrainStop! Line is a train line
[INFO] Line 9999: IS a train line
[INFO] Train preference: 1 train lines free, 1 non-train lines DISABLED
```

**If you see `Train=false` for ALL waypoints on your train line**, that means:
- The component isn't named `TrainStop`
- OR the component isn't attached to waypoints
- OR we need to check a different entity/component

## Next Steps

1. ✅ Build completed with debug logging
2. ⏳ Run game and check logs
3. ⏳ Share the debug output from "Checking line..." messages
4. ⏳ I'll fix the `IsTrainLine()` logic based on what components actually exist

The debug logging will definitively show us what's wrong with the train line detection!
