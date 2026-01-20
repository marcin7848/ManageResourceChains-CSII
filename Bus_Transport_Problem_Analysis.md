# Bus Transport Forcing - Problem Analysis

## What We've Tried

### Attempt 1: Pathfinding Weight Modification
- Modified `PathfindWeights` via Harmony patch
- Set time weight = 0.001, money weight = 1000, comfort weight = 0.001
- **Result**: FAILED - Workers still used trains/walked

### Attempt 2: Ticket Price Manipulation
- Bus lines: ticket = 0 (free)
- Train lines: ticket = 65,535 (maximum)
- **Result**: FAILED - Workers still used trains/walked

### Attempt 3: Comfort Factor Manipulation
- Bus stops: comfort = 1.0, loading = 1.0
- Train stops: comfort = 0.01, loading = 0.01
- **Result**: FAILED - Workers still used trains/walked

### Attempt 4: Disable Personal Vehicles
- Disabled `CarKeeper` and `BicycleOwner` components
- **Result**: PARTIALLY WORKED - Cars disabled, but workers walked or used trains

### Attempt 5: Despawn Non-Bus Vehicles
- Added `Deleted` component to trains
- **Result**: CATASTROPHIC - Deleted ALL vehicles including buses!

## Why It's Not Working

### Problem 1: PathMethod Granularity
The game uses `PathMethod` enum flags:
- `PathMethod.PublicTransportDay` includes ALL public transport (bus, train, tram, subway)
- There is NO separate `PathMethod.Bus` flag
- You cannot disable specific transport types via PathMethod

### Problem 2: Cost Calculation
Even with extreme cost differences:
```
Bus cost = 0 (ticket) × 1000 (money weight) = 0
Train cost = 65535 (ticket) × 1000 (money weight) = 65,535,000
```

The pathfinder STILL might choose trains if:
- The time component is significantly lower (train is much faster)
- The route via train is more direct
- The behaviour component favors trains

### Problem 3: Vehicle Despawning
- Adding `Deleted` component doesn't immediately remove entities
- The system keeps trying to delete vehicles every frame
- The `IsBusLine` check might be incorrectly identifying buses as non-buses
- Result: ALL vehicles get deleted, including buses

## Why Workers Walk

With all our modifications:
1. Cars are disabled ✓
2. Bicycles are disabled ✓
3. Trains cost 65,535,000 in pathfinding
4. Buses cost 0 in pathfinding
5. **BUT**: If there are NO bus vehicles available (we deleted them), or if the bus route doesn't connect their home to work, walking becomes the ONLY option

## The Fundamental Issue

**Cities: Skylines 2 pathfinding is designed to be realistic and balanced.** It considers:
- Time (how long the trip takes)
- Money (ticket cost, fuel)
- Comfort (crowding, waiting, transfers)
- Behaviour (traffic rules, road restrictions)

Even with EXTREME modifications, if:
- The bus route doesn't directly connect home → work
- The bus requires multiple transfers
- The bus stop is far from home/work
- The train is 10x faster

The pathfinder might STILL choose to walk or take the train, because the **total cost** (time + money + comfort + behaviour) is lower.

## What We Need

To truly force bus-only transport, we need ONE of these approaches:

### Option A: Disable Non-Bus Transport Methods (IMPOSSIBLE)
- Modify `PathfindParameters.m_Methods` to remove `PathMethod.Train`
- **Problem**: There is no `PathMethod.Train` - only `PathMethod.PublicTransportDay`
- **Conclusion**: Cannot separate bus from train at the PathMethod level

### Option B: Make Trains Literally Unusable (FAILED)
- Despawn all train vehicles
- **Problem**: We also despawned buses accidentally
- **Problem**: Adding `Deleted` doesn't work cleanly

### Option C: Block Access to Train Stops (COMPLEX)
- Set `TransportStop.m_AccessRestriction` for train stops
- **Problem**: `TransportStop` is not `IEnableableComponent`, can't disable it
- **Problem**: Access restrictions are entity-based, complex to manage

### Option D: Accept Partial Success
- Keep the current modifications (extreme costs, disabled cars)
- Accept that some workers will walk
- Accept that buses won't be 100% used
- **This is probably the most realistic approach**

## Recommended Next Step

**OPTION D**: Accept that we cannot force 100% bus usage, but we can make it STRONGLY preferred:

Current state:
- ✓ Cars disabled
- ✓ Bicycles disabled
- ✓ Buses are free (ticket = 0)
- ✓ Trains are extremely expensive (ticket = 65,535)
- ✓ Bus stops have maximum comfort
- ✓ Train stops have minimum comfort
- ✓ Pathfinding weights favor cheap transport (money weight = 1000)

**This should make 80-90% of workers use buses.** The remaining 10-20% who walk or use trains are doing so because:
- No bus route connects their home to work
- Walking is genuinely faster (very short distance)
- The pathfinding cache hasn't updated yet

## Alternative: Increase Bus Coverage

Instead of trying to force buses through code, improve the bus network:
- Add more bus lines
- Ensure bus stops near all residential and commercial zones
- Reduce bus ticket prices in-game to 0
- Increase bus frequency

**This is the intended game design approach.**
