# Healthcare Service Rules - Implementation Guide

## Overview

This guide provides step-by-step instructions for implementing healthcare service rules (ambulances and hearses) as a prototype for the broader service rules system.

## Implementation Status

### ✅ Completed
1. **Data structures extended** - Added `ServiceType` and `ResourceType` enums
2. **ResourceChainRule extended** - Added `ServiceSubtype` and `ResourceSubtype` fields
3. **Rule checking methods added** - `IsServiceTransportAllowed()` in ResourceChainRulesSystem
4. **Service intercept system created** - ServiceRulesInterceptSystem with healthcare checks
5. **Patch framework created** - ServicePathfindingPatches (template)

### 🔨 To Be Implemented
1. **Pathfinding interception** - Actual Harmony patches for healthcare
2. **UI updates** - Support for service type selection
3. **Testing framework** - Validate healthcare rules work correctly
4. **Extension to other services** - Fire, Police, Garbage, etc.

## How Healthcare Pathfinding Works

### Game Flow for Ambulance Dispatch

```
1. Citizen gets sick/injured
   └─> HealthProblem component added to citizen entity

2. HealthcareDispatchSystem runs
   └─> Creates HealthcareRequest entity
   └─> Links citizen to request

3. PathfindSetupSystem processes HealthcareRequest
   └─> Calls HealthcarePathfindSetup.SetupAmbulancesJob
   
4. SetupAmbulancesJob.Execute() runs
   └─> Iterates through hospitals
   └─> For each hospital, checks:
       - Is hospital in correct ServiceDistrict? (AreaUtils.CheckServiceDistrict)
       - Does hospital have available ambulances?
   └─> If yes, calls targetSeeker.FindTargets(hospitalEntity, cost)
   └─> This adds hospital as potential target for pathfinding

5. Pathfinding occurs
   └─> Finds best path from hospital to citizen location

6. AmbulanceAISystem dispatches vehicle
   └─> Ambulance follows path to pick up citizen
   └─> Transports citizen to hospital
```

### Key Interception Point

**BEST PLACE TO INTERCEPT: Step 4 - Before `targetSeeker.FindTargets()` is called**

In `HealthcarePathfindSetup.SetupAmbulancesJob.Execute()`:

```csharp
// Current game code (simplified)
for (int i = 0; i < hospitals.Length; i++)
{
    Entity hospital = hospitals[i];
    
    if (AreaUtils.CheckServiceDistrict(targetBuilding, hospital, serviceDistricts))
    {
        if ((hospital.Flags & HospitalFlags.HasAvailableAmbulances) != 0)
        {
            // THIS IS WHERE WE NEED TO INTERCEPT
            // Add our rule check here before FindTargets
            targetSeeker.FindTargets(hospital, cost);
        }
    }
}
```

**Our modification:**

```csharp
for (int i = 0; i < hospitals.Length; i++)
{
    Entity hospital = hospitals[i];
    
    if (AreaUtils.CheckServiceDistrict(targetBuilding, hospital, serviceDistricts))
    {
        if ((hospital.Flags & HospitalFlags.HasAvailableAmbulances) != 0)
        {
            // CHECK OUR RULES FIRST
            bool rulesAllow = CheckHealthcareRules(hospital, targetBuilding);
            
            if (rulesAllow)
            {
                targetSeeker.FindTargets(hospital, cost);
            }
            // If rules don't allow, skip FindTargets - hospital won't be considered
        }
    }
}
```

## Implementation Approach

### Option 1: Harmony Transpiler Patch (Most Powerful)

**Pros:**
- Can inject code at exact location needed
- Minimal performance impact
- Clean integration

**Cons:**
- Complex to implement (IL code manipulation)
- Harder to debug
- May break with game updates

**How it works:**
- Use Harmony transpiler to inject IL instructions
- Add rule check before `FindTargets` call
- Skip `FindTargets` if rules block service

### Option 2: Harmony Prefix Patch (Simpler)

**Pros:**
- Easier to implement
- More maintainable
- Easier to debug

**Cons:**
- Less precise control
- Harder to modify middle of method

**How it works:**
- Patch the entire `Execute` method
- Return false to skip original method
- Implement our own version with rule checks

### Option 3: Custom ECS System (Cleanest)

**Pros:**
- No Harmony patches needed
- Most maintainable
- Best performance

**Cons:**
- Requires running after pathfinding
- Might be too late to prevent dispatch

**How it works:**
- Create system that runs after PathfindSetupSystem
- Check pathfinding results
- Remove/invalidate paths that violate rules

## Recommended Implementation: Hybrid Approach

Combine Option 2 and 3:

1. **Custom ECS System** runs first and marks service requests
2. **Lightweight Prefix Patch** checks marks and skips if needed
3. **Fallback validation** in dispatch system if needed

### Step-by-Step Implementation

#### Step 1: Add Service Request Marking Component

```csharp
// Add to ResourceChainConfig.cs

/// <summary>
/// Component to mark service requests that have been validated against rules
/// </summary>
public struct ServiceRuleValidation : IComponentData
{
    public bool IsAllowed;
    public ServiceType ServiceType;
    public Entity ServiceBuilding;
    public Entity TargetBuilding;
}
```

#### Step 2: Create Pre-Validation System

```csharp
// Create: ServiceRequestValidationSystem.cs

[UpdateBefore(typeof(PathfindSetupSystem))]
public partial class ServiceRequestValidationSystem : GameSystemBase
{
    private ResourceChainRulesSystem m_RulesSystem;
    
    protected override void OnCreate()
    {
        m_RulesSystem = World.GetOrCreateSystemManaged<ResourceChainRulesSystem>();
    }
    
    protected override void OnUpdate()
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        
        // Process healthcare requests
        Entities
            .WithAll<HealthcareRequest>()
            .WithNone<ServiceRuleValidation>()
            .ForEach((Entity entity, in HealthcareRequest request) =>
            {
                // Mark as validated (will be checked during pathfinding)
                ecb.AddComponent<ServiceRuleValidation>(entity, new ServiceRuleValidation
                {
                    IsAllowed = false, // Will be determined per hospital
                    ServiceType = ServiceType.Healthcare,
                    ServiceBuilding = Entity.Null, // Not known yet
                    TargetBuilding = GetTargetBuilding(request.m_Citizen)
                });
            }).WithoutBurst().Run();
            
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
```

#### Step 3: Implement Harmony Patch

```csharp
// Update ServicePathfindingPatches.cs

private static void PatchHealthcarePathfinding(Harmony harmony)
{
    // Get the job struct type
    var setupAmbulancesJobType = AccessTools.TypeByName(
        "Game.Simulation.HealthcarePathfindSetup+SetupAmbulancesJob");
    
    if (setupAmbulancesJobType == null)
    {
        Mod.log.Error("Could not find SetupAmbulancesJob type");
        return;
    }
    
    // Get the Execute method
    var executeMethod = AccessTools.Method(setupAmbulancesJobType, "Execute");
    
    if (executeMethod == null)
    {
        Mod.log.Error("Could not find Execute method");
        return;
    }
    
    // Create wrapper that checks rules
    var wrapper = AccessTools.Method(
        typeof(ServicePathfindingPatches), 
        nameof(HealthcareExecuteWrapper));
    
    harmony.Patch(executeMethod, prefix: new HarmonyMethod(wrapper));
    
    Mod.log.Info("Healthcare pathfinding patched successfully");
}

private static bool HealthcareExecuteWrapper(
    object __instance,
    // Add actual parameters from Execute method
    )
{
    // Call our custom implementation that includes rule checks
    return ExecuteWithRuleChecks(__instance);
}

private static bool ExecuteWithRuleChecks(object jobInstance)
{
    // This would contain a modified version of the Execute logic
    // that includes our rule checks before calling FindTargets
    
    // For now, return true to allow original execution
    return true;
}
```

## Testing Plan

### Test 1: Basic Healthcare Rule

**Setup:**
1. Create Hospital A in city center
2. Create Hospital B in suburbs
3. Create residential buildings in both areas
4. Add rule: Hospital A → ALLOW only city center district

**Test:**
1. Make citizen in city center sick
2. Verify: Hospital A ambulance responds ✓
3. Verify: Hospital B ambulance doesn't respond ✓

4. Make citizen in suburbs sick
5. Verify: Hospital A ambulance doesn't respond ✓
6. Verify: Hospital B ambulance responds ✓

### Test 2: Building-Specific Rule

**Setup:**
1. Create Hospital A
2. Create 5 residential buildings
3. Add rule: Hospital A → ALLOW only Building 1, 2, 3

**Test:**
1. Make citizen in Building 1 sick → Hospital A responds ✓
2. Make citizen in Building 4 sick → Hospital A doesn't respond ✓
3. Make citizen in Building 5 sick → Hospital A doesn't respond ✓

### Test 3: DISALLOW Rule

**Setup:**
1. Create Hospital A
2. Create industrial district
3. Create residential district
4. Add rule: Hospital A → DISALLOW industrial district

**Test:**
1. Citizen in residential area → Hospital A responds ✓
2. Worker in industrial building → Hospital A doesn't respond ✓

## Performance Considerations

### Optimization Strategies

1. **Cache rule lookups**
   - Store evaluated rules in NativeHashMap
   - Update only when rules change
   - Reuse between frames

2. **Batch validation**
   - Process multiple requests together
   - Use Burst compilation
   - Minimize managed code

3. **Early exit**
   - Check if any rules exist first
   - Skip validation if no rules configured
   - Fast path for common case

4. **Incremental updates**
   - Don't validate every frame
   - Only check new requests
   - Mark validated requests

### Expected Performance Impact

- **No Rules Configured**: ~0.1% overhead (single if check)
- **Few Rules (<10)**: ~0.5% overhead (cache hit)
- **Many Rules (>100)**: ~2% overhead (cache + evaluation)
- **Complex Rules**: ~5% overhead (multiple evaluations)

## Next Steps

1. ✅ Create data structures (DONE)
2. ✅ Create rule checking methods (DONE)
3. ✅ Create intercept system framework (DONE)
4. 🔨 Implement actual Harmony patch for healthcare
5. 🔨 Test with sample rules
6. 🔨 Fix any issues found
7. 🔨 Add UI support
8. 🔨 Extend to other services

## Code Locations

### Files Modified
- ✅ `Data/ResourceChainConfig.cs` - Added service enums
- ✅ `Systems/ResourceChainRulesSystem.cs` - Added service checking methods

### Files Created
- ✅ `Systems/ServiceRulesInterceptSystem.cs` - Service validation
- ✅ `Systems/ServicePathfindingPatches.cs` - Harmony patches (template)

### Files To Create
- 🔨 `Systems/ServiceRequestValidationSystem.cs` - Pre-validation
- 🔨 `Tests/HealthcareRulesTests.cs` - Unit tests

## Troubleshooting

### Issue: Rules not being applied

**Check:**
1. Are rules properly configured in ResourceChainManagementSystem?
2. Is ServiceRulesInterceptSystem created?
3. Are Harmony patches applied successfully?
4. Check logs for errors

**Debug:**
```csharp
// Add logging to IsServiceTransportAllowed
Mod.log.Info($"Checking service: {serviceType} from {serviceBuildingId} to {targetBuildingId}");
Mod.log.Info($"Result: {isAllowed}");
```

### Issue: Game crashes when ambulance dispatched

**Possible causes:**
1. Harmony patch corrupted method
2. Invalid entity references
3. Null reference in rule checking

**Solutions:**
1. Remove Harmony patches temporarily
2. Add null checks
3. Validate entities exist before checking

### Issue: Performance degradation

**Check:**
1. How many rules are configured?
2. Are rules being cached?
3. Is Burst compilation enabled?

**Optimize:**
1. Reduce number of rules
2. Use district rules instead of building rules
3. Increase validation interval

## Future Enhancements

### Phase 2: Additional Services
- Fire stations and fire engines
- Police stations and police cars
- Garbage facilities and trucks
- Post offices and vans

### Phase 3: Advanced Features
- Time-based rules (different rules at different times)
- Priority-based service assignment
- Load balancing between service buildings
- Service area visualization in UI

### Phase 4: Resource Transport
- Factory to factory transport rules
- Warehouse restrictions
- Import/export controls
- Resource type filtering

## References

- `ServiceAndResourceRules.md` - Complete system documentation
- Game decompiled code in `other/Decompiled/Game.Simulation/`
- Harmony documentation: https://harmony.pardeike.net/
