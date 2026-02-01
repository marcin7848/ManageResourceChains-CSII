# Critical Issue Found - Burst Compilation Blocks Harmony Patches

## The Problem

The service pathfinding code (`PolicePathfindSetup`, `FirePathfindSetup`, etc.) is **Burst-compiled**:

```csharp
[BurstCompile]
private struct SetupPolicePatrolsJob : IJobChunk
{
    // ...
    if (AreaUtils.CheckServiceDistrict(entity2, entity, m_ServiceDistricts))
    {
        // Dispatch service
    }
}
```

**Burst compilation converts C# code to highly optimized native code**. When Burst compiles the job:
1. It inlines `AreaUtils.CheckServiceDistrict()` directly into the native code
2. Harmony patches on the C# method are **completely bypassed**
3. Our postfix methods **never get called** during actual gameplay

This explains why:
- ✅ Patches apply successfully (we patched the C# method)
- ❌ No log messages appear (Burst code doesn't call the C# method)
- ❌ Rules don't work (our code never runs)

## Why This Happened

Cities Skylines 2 uses Burst for performance. All pathfinding and simulation jobs are Burst-compiled to run as fast as possible. **This is by design and cannot be changed.**

## Solution Options

### Option 1: Intercept ServiceDistrict Configuration (RECOMMENDED)
Instead of patching the check, **modify the ServiceDistrict buffer** on service buildings to include/exclude districts based on our rules.

**How it works:**
1. Game's ServiceDistrictSystem sets up which districts each service building serves
2. We create a system that runs AFTER and modifies the ServiceDistrict buffers
3. The Burst code reads these buffers and naturally respects our changes
4. No Harmony patches needed!

**Implementation:**
```csharp
// In a system that runs after ServiceDistrictSystem:
foreach (var policeStation in policeStations)
{
    var serviceDistricts = GetBuffer<ServiceDistrict>(policeStation);
    
    // Apply our rules by modifying the buffer
    // Add districts that are allowed
    // Remove districts that are disallowed
}
```

### Option 2: Post-Dispatch Cancellation
Intercept service vehicles AFTER they're dispatched and cancel invalid ones.

**How it works:**
1. Let vanilla pathfinding run normally
2. After dispatch, check ServiceDispatch components on vehicles
3. Remove dispatches that violate rules
4. Vehicle returns to station

**Pros:** Works for individual building restrictions
**Cons:** Wastes pathfinding computation, vehicles may start moving before cancellation

### Option 3: Prefix Patch on Job Schedule (COMPLEX)
Patch the system's OnUpdate method BEFORE the Burst job is scheduled, modify the input data.

**Pros:** Can work around Burst
**Cons:** Very complex, fragile, performance impact

## Recommended Implementation: Service District Manipulation

This is the cleanest solution that works WITH the game's architecture instead of against it.

###Step 1: Create ServiceDistrictManipulationSystem

```csharp
[UpdateAfter(typeof(ServiceDistrictSystem))]
public partial class ServiceDistrictManipulationSystem : GameSystemBase
{
    private ResourceChainRulesSystem m_RulesSystem;
    
    protected override void OnUpdate()
    {
        // For each service building with rules:
        //   1. Get its ServiceDistrict buffer
        //   2. Clear it
        //   3. Add only allowed districts based on rules
        //   4. Burst code will naturally respect this
    }
}
```

### Step 2: Convert Rules to Service Districts

When a rule says "Police Station A can only serve District North":
1. Clear Police Station A's ServiceDistrict buffer
2. Add District North to the buffer
3. Burst pathfinding code reads the buffer and only considers District North

This works because `AreaUtils.CheckServiceDistrict()` just checks if a district is in the ServiceDistrict buffer!

## Why This Is Better

1. **Works with Burst** - No patching needed
2. **Uses vanilla systems** - ServiceDistrict is how the game does this
3. **Better performance** - Burst code stays optimized
4. **More reliable** - No Harmony fragility
5. **Matches game design** - This is how service districts are meant to work

## Current Status

- ❌ Harmony patch approach: **CANNOT WORK** with Burst-compiled code
- ✅ Service District manipulation: **WILL WORK** with Burst code
- 🔄 Need to implement: ServiceDistrictManipulationSystem

## Next Steps

1. Create `ServiceDistrictManipulationSystem`
2. Run after `ServiceDistrictSystem`  
3. For each building with service rules, manipulate its `ServiceDistrict` buffer
4. Remove all Harmony patches (not needed)
5. Test with police station

---

**Date:** February 1, 2026  
**Issue:** Burst compilation prevents Harmony patches  
**Solution:** Manipulate ServiceDistrict buffers instead
**Status:** Need to implement new approach
