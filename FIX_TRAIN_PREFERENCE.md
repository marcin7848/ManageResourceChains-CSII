# Fix: Train Preference Not Working

## Problem Identified

When you set `TransportPreferenceSystem.DefaultPreference = PreferredTransportMethod.Train`, workers were still using buses instead of trains.

### Root Cause

**The `TransportPriorityCostSystem` only had implementation for Bus preference!**

Looking at the code (line 106-115):
```csharp
if (preference == PreferredTransportMethod.Bus ||
    preference == PreferredTransportMethod.PublicTransport)
{
    ApplyBusPreference();  // Make buses free, trains expensive
    ModifyStopComfort();    // Make bus stops comfortable
    DisablePersonalVehicles();
}
// NO CODE FOR TRAIN PREFERENCE!
```

**What Was Happening**:
1. You set preference to `Train`
2. System checked: "Is it Bus or PublicTransport?" → **NO**
3. System did nothing - no ticket price changes, no comfort changes
4. Workers used **default vanilla pathfinding**
5. They chose buses because **bus stop was closer** (less walking)

### Why Bus Stop Proximity Mattered

With Train preference but no enforcement:
- Bus ticket: normal price (~5-10)
- Train ticket: normal price (~5-10)
- Both equally viable in cost

Pathfinding chose based on:
```
Bus route:  Walking 100m + Bus ride 500m = Total 600m
Train route: Walking 300m + Train ride 500m = Total 800m
```

**Result**: Bus wins because less walking! The proximity/walking distance dominated the decision.

---

## Solution Implemented

Added complete Train preference implementation, mirroring the Bus preference logic but inverted:

### 1. Added `IsTrainLine()` Helper Method

```csharp
private bool IsTrainLine(Entity lineEntity)
{
    // Checks if route uses TrainStop components
    // Same logic as IsBusLine but for trains
}
```

### 2. Added `ApplyTrainPreference()` Method

```csharp
private void ApplyTrainPreference()
{
    foreach (transport line)
    {
        if (IsTrainLine(line))
        {
            // Make train FREE
            line.m_TicketPrice = 0;
        }
        else
        {
            // Make non-train EXPENSIVE
            line.m_TicketPrice = 65535;
            line.m_VehicleInterval = 10000f; // Disable spawning
        }
    }
}
```

### 3. Added `ModifyStopComfortForTrain()` Method

```csharp
private void ModifyStopComfortForTrain()
{
    foreach (stop)
    {
        if (HasComponent<TrainStop>(stop))
        {
            // Perfect comfort for train stops
            stop.m_ComfortFactor = 1.0f;
            stop.m_LoadingFactor = 1.0f;
        }
        else
        {
            // Terrible comfort for non-train stops
            stop.m_ComfortFactor = 0.01f;
            stop.m_LoadingFactor = 0.01f;
        }
    }
}
```

### 4. Updated `OnUpdate()` to Call Train Methods

```csharp
if (preference == PreferredTransportMethod.Train)
{
    ApplyTrainPreference();
    ModifyStopComfortForTrain();
    DisablePersonalVehicles();
    _modificationsApplied = true;
}
```

---

## How It Works Now

### With Train Preference Enabled:

**1. Ticket Prices** (every 128 frames):
- Train lines: **0** (FREE)
- Bus lines: **65,535** (MAXIMUM)
- Tram/Metro/Subway: **65,535** (MAXIMUM)

**2. Stop Comfort**:
- Train stops: **1.0** comfort (perfect)
- Bus stops: **0.01** comfort (terrible)
- Other stops: **0.01** comfort (terrible)

**3. Personal Vehicles**:
- Cars: **DISABLED** (CarKeeper component disabled)
- Bicycles: **DISABLED** (BicycleOwner component disabled)

**4. Pathfinding Weights** (from TransportPreferencePatches):
- Time weight: **0.001** (time doesn't matter)
- Money weight: **1000** (money matters EXTREMELY)
- Comfort weight: **0.001** (comfort doesn't matter)

### Cost Calculation Example:

```
Train Route (300m walk):
- Time: 600s × 0.001 = 0.6
- Money: 0 (ticket) × 1000 = 0
- Comfort: 1.0 × 0.001 = 0.001
- Walking: 300m = minimal cost
TOTAL: ~0.6

Bus Route (100m walk):
- Time: 500s × 0.001 = 0.5
- Money: 65,535 (ticket) × 1000 = 65,535,000
- Comfort: 0.01 × 0.001 = 0.00001
- Walking: 100m = minimal cost
TOTAL: ~65,535,000
```

**Train wins by 65 MILLION points!** Even though bus stop is closer, the massive ticket price penalty makes it impossible to choose.

---

## Testing

Run the game with `DefaultPreference = PreferredTransportMethod.Train` and you should see in logs:

```
[INFO] Train preference: X train lines free, Y non-train lines DISABLED
[INFO] Modified stop comfort: X train stops maximized, Y other stops minimized
[INFO] Disabled personal vehicles: X cars, Y bicycles
```

Workers should now:
- ✅ Use trains exclusively when available
- ✅ Ignore buses even if closer
- ❌ Not use cars (disabled)
- ❌ Not use bicycles (disabled)
- ⚠️ Walk if no train connection exists

---

## Why Workers Were Using Buses Before

**Summary of the issue**:

1. ❌ **No Train enforcement logic existed** in TransportPriorityCostSystem
2. ✅ Only Bus and PublicTransport had implementation
3. ❌ Train preference = NO changes to tickets/comfort/vehicles
4. ✅ Workers used vanilla pathfinding
5. ✅ Vanilla pathfinding chose **closest stop** = bus stop
6. ✅ Proximity/walking distance dominated the decision

**It wasn't a bug in pathfinding - the system simply wasn't enforcing train preference at all!**

---

## Future Extensibility

The pattern is now established. To add Metro/Tram/Subway preferences:

1. Add `IsMetroLine()` / `IsTramLine()` helper
2. Add `ApplyMetroPreference()` / `ApplyTramPreference()` method
3. Add `ModifyStopComfortForMetro()` / `ModifyStopComfortForTram()` method
4. Add `else if` branch in `OnUpdate()` for the new preference

The same 5-layer enforcement applies:
- Ticket prices (preferred = 0, others = 65535)
- Stop comfort (preferred = 1.0, others = 0.01)
- Pathfinding weights (money × 1000)
- Car disabling
- Bicycle disabling

---

## Files Modified

- `Code/ManageResourceChains/Systems/TransportPriorityCostSystem.cs`
  - Added `IsTrainLine()` method
  - Added `ApplyTrainPreference()` method
  - Added `ModifyStopComfortForTrain()` method
  - Updated `OnUpdate()` to handle Train preference

**Build Status**: ✅ Successful (0 errors, 2 warnings)

**Deployment**: ✅ DLL deployed to mods folder

---

## TL;DR

**Before**: Train preference did nothing → workers used closest stop (bus)  
**After**: Train preference enforced → trains free, buses cost 65,535,000 → workers MUST use trains

The fix mirrors the existing bus preference logic but inverted for trains.
