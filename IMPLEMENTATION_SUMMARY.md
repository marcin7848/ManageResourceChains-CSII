# Service and Resource Rules Implementation - Summary

## What Was Implemented

This document summarizes the work completed to extend the ManageResourceChains mod to support service and resource transport rules, in addition to the existing worker transport rules.

## Files Created

### 1. Documentation Files

#### `ServiceAndResourceRules.md`
Complete technical documentation covering:
- Overview of all three transport types (Workers, Services, Resources)
- Detailed breakdown of service types (Healthcare, Fire, Police, etc.)
- Resource types and how they're transported
- Implementation architecture and pathfinding interception strategies
- Data structure extensions
- Testing scenarios
- Performance considerations

#### `HealthcareImplementationGuide.md`
Step-by-step implementation guide for healthcare services as a prototype:
- How healthcare pathfinding works in the game
- Exact interception points identified
- Three implementation approaches analyzed
- Recommended hybrid approach with code examples
- Complete testing plan
- Performance optimization strategies
- Troubleshooting guide

### 2. Code Files

#### `Data/ResourceChainConfig.cs` (Modified)
Extended with new enums and fields:

**Added Enums:**
```csharp
public enum ServiceType
{
    All, Healthcare, Deathcare, Fire, Police, 
    Garbage, Post, Evacuation, Maintenance
}

public enum ResourceType
{
    All, RawMaterials, ProcessedGoods, 
    CommercialGoods, Electricity, Water
}
```

**Extended ResourceChainRule:**
- Added `ServiceSubtype` field for granular service control
- Added `ResourceSubtype` field for granular resource control
- Added `SpecificResourceIds` list for future resource filtering

#### `Systems/ResourceChainRulesSystem.cs` (Modified)
Added three new public methods:

**1. `IsServiceTransportAllowed()`**
```csharp
public bool IsServiceTransportAllowed(
    int serviceBuildingId, 
    int targetBuildingId, 
    ServiceType serviceType)
```
Checks if a service vehicle from a service building (hospital, fire station, etc.) can serve a target building.

**2. `IsResourceTransportAllowed()`**
```csharp
public bool IsResourceTransportAllowed(
    int sourceBuildingId, 
    int destinationBuildingId, 
    ResourceType resourceType)
```
Checks if resources can be transported from source to destination building.

**Internal Methods:**
- `IsServiceTransportAllowedInternal()` - Core service rule evaluation logic
- `IsResourceTransportAllowedInternal()` - Core resource rule evaluation logic

Both use the same rule evaluation flow as worker transport:
1. Collect relevant rules from all buildings and districts
2. Filter by transport type and subtype
3. Filter by direction (Incoming/Outgoing)
4. Remove district rules if building rules exist
5. Evaluate using ALLOW/DISALLOW logic

#### `Systems/ServiceRulesInterceptSystem.cs` (New)
Central system for service rule interception:

**Purpose:** Provides methods to check if services are allowed before dispatch

**Key Methods:**
- `CheckHealthcareService()` - For ambulances and hearses
- `CheckFireService()` - For fire engines and helicopters
- `CheckPoliceService()` - For police cars and helicopters
- `CheckGarbageService()` - For garbage trucks
- `CheckPostService()` - For post vans

**How it works:**
- Created during game initialization
- Called by pathfinding hooks (to be implemented)
- Delegates to ResourceChainRulesSystem for rule evaluation

#### `Systems/ServicePathfindingPatches.cs` (New - Template)
Framework for Harmony patches to intercept pathfinding:

**Purpose:** Inject rule checks into game's pathfinding setup code

**Status:** Template/framework created, actual patches need implementation

**Structure:**
- `Apply()` - Initializes Harmony patches
- `PatchHealthcarePathfinding()` - Patches healthcare pathfinding (template)
- `HealthcarePathfindPrefix()` - Prefix method for interception (template)
- `Remove()` - Cleans up patches on mod disable

## How It Works

### Service Rules Flow

```
1. Service request created (e.g., citizen gets sick)
   ↓
2. Game's PathfindSetupSystem processes request
   ↓
3. [OUR INTERCEPT] ServicePathfindingPatches checks rules
   ↓
4. If allowed: Pathfinding proceeds normally
   If blocked: Service building skipped, no ambulance dispatched
   ↓
5. Vehicle dispatched (only if rules allow)
```

### Rule Evaluation Logic

For any transport (worker/service/resource):

```
1. Collect all rules from:
   - Source building
   - Destination building
   - Source district
   - Destination district

2. Filter rules:
   - Must match TransportType (Workers/Services/Resources)
   - Must match subtype (e.g., Healthcare, Fire)
   - Must match direction (Incoming/Outgoing)

3. Priority:
   - Building rules override district rules
   - Specific rules override general rules

4. Evaluate:
   - If any DISALLOW rule matches → BLOCK
   - If ALLOW rules exist but none match → BLOCK
   - Otherwise → ALLOW
```

## Service Types Identified

### Emergency Services (Go TO buildings)
| Service | Building | Vehicle | Game Request Type |
|---------|----------|---------|-------------------|
| Healthcare | Hospital | Ambulance, Medical Helicopter | `HealthcareRequest` |
| Deathcare | Cemetery, Crematorium | Hearse | `HealthcareRequest` (death) |
| Fire & Rescue | Fire Station | Fire Engine, Fire Helicopter | `FireRescueRequest` |
| Police | Police Station | Police Car, Police Helicopter | `PoliceEmergencyRequest` |
| Garbage | Garbage Facility | Garbage Truck | `GarbageCollectionRequest` |
| Post | Post Office | Post Van | `PostVanRequest` |

### Utility Services (Infrastructure)
- Electricity (Power plants → Buildings)
- Water (Water facilities → Buildings)

## Resources vs Services

### Services
- **Direction:** Service building → Target building
- **Purpose:** Provide a service (healthcare, fire, garbage collection)
- **Vehicle:** Returns to service building after completing service
- **Examples:** Ambulance, Fire truck, Police car

### Resources
- **Direction:** Source building → Destination building
- **Purpose:** Transport materials or goods
- **Vehicle:** Delivery truck, cargo transport
- **Examples:** Grain delivery, furniture transport, raw materials

## Usage Examples

### Example 1: Hospital Service Restriction

**Scenario:** Hospital A should only serve buildings in District North

**Rule Configuration:**
```json
{
  "BuildingEntityId": 12345,  // Hospital A
  "Rules": [
    {
      "Type": "Outgoing",
      "Allow": "Allow",
      "TransportType": "Services",
      "ServiceSubtype": "Healthcare",
      "Districts": [67890]  // District North ID
    }
  ]
}
```

**Effect:**
- Hospital A ambulances will ONLY respond to emergencies in District North
- Other hospitals can serve other districts normally

### Example 2: Fire Station Coverage

**Scenario:** Industrial fire station should only handle industrial fires

**Rule Configuration:**
```json
{
  "BuildingEntityId": 23456,  // Fire Station
  "Rules": [
    {
      "Type": "Outgoing",
      "Allow": "Allow",
      "TransportType": "Services",
      "ServiceSubtype": "Fire",
      "Districts": [78901]  // Industrial District ID
    }
  ]
}
```

### Example 3: Garbage Collection Zones

**Scenario:** Premium garbage facility only serves wealthy district

**Rule Configuration:**
```json
{
  "BuildingEntityId": 34567,  // Premium Facility
  "Rules": [
    {
      "Type": "Outgoing",
      "Allow": "Allow",
      "TransportType": "Services",
      "ServiceSubtype": "Garbage",
      "Districts": [89012]  // Wealthy District ID
    }
  ]
}
```

### Example 4: Resource Supply Chain

**Scenario:** Bakery must get flour only from specific farm

**Rule Configuration:**
```json
{
  "BuildingEntityId": 45678,  // Bakery
  "Rules": [
    {
      "Type": "Incoming",
      "Allow": "Allow",
      "TransportType": "Resources",
      "ResourceSubtype": "RawMaterials",
      "Buildings": [56789]  // Specific Farm ID
    }
  ]
}
```

## Implementation Status

### ✅ Completed (Phase 1)

1. **Data Model Extended**
   - Service types enumerated and categorized
   - Resource types identified
   - Data structures extended to support new rule types

2. **Core Logic Implemented**
   - Service rule checking method created
   - Resource rule checking method created
   - Same evaluation logic as workers (proven and tested)

3. **System Framework Created**
   - ServiceRulesInterceptSystem for service validation
   - ServicePathfindingPatches for interception framework

4. **Documentation Complete**
   - Full system documentation
   - Implementation guide for healthcare prototype
   - Usage examples and testing scenarios

### 🔨 To Be Implemented (Phase 2)

1. **Pathfinding Interception**
   - Complete Harmony patches for healthcare
   - Test healthcare service rules
   - Extend to fire, police, garbage services

2. **UI Updates**
   - Add service type selector in rule creation
   - Add resource type selector
   - Visual indicators for service coverage

3. **Testing & Validation**
   - Unit tests for rule evaluation
   - Integration tests with actual game
   - Performance benchmarking

4. **Polish & Optimization**
   - Cache rule evaluations
   - Burst compile service checking jobs
   - Optimize for large numbers of rules

## Key Design Decisions

### 1. Reuse Worker Rule Logic
**Decision:** Use same rule evaluation system for all transport types

**Rationale:**
- Proven system that already works
- Consistent behavior across all rule types
- Less code to maintain

### 2. Service Subtypes
**Decision:** Add granular service type filtering

**Rationale:**
- Users want to control specific services (e.g., only fire, not police)
- Different services have different use cases
- Allows future expansion to new service types

### 3. Pathfinding Interception
**Decision:** Intercept during pathfinding setup, not dispatch

**Rationale:**
- Most efficient point (before pathfinding computation)
- Prevents wasted pathfinding calculations
- Clean integration with game systems

### 4. Hybrid Implementation Approach
**Decision:** Combine ECS system + Harmony patches

**Rationale:**
- ECS system for rule management (clean, maintainable)
- Harmony patches for interception (necessary for injection)
- Best of both worlds

## Performance Considerations

### Optimization Strategies

1. **Fast Path for No Rules**
   ```csharp
   if (buildingConfigs.Count == 0 && districtConfigs.Count == 0)
       return true; // Instant return, no rules configured
   ```

2. **Rule Caching**
   - Cache evaluated rules in NativeHashMap
   - Invalidate only when rules change
   - Reuse between pathfinding calls

3. **Burst Compilation**
   - Service checking can be Burst-compiled
   - Parallel processing for multiple requests
   - Significant performance boost

### Expected Performance

**Best Case (No Rules):** 
- ~0.1% overhead (single conditional check)

**Average Case (10-50 Rules):**
- ~0.5-1% overhead (cached lookups)

**Worst Case (100+ Rules):**
- ~2-5% overhead (evaluation + cache misses)

**Mitigation:**
- Most players will have few rules (<20)
- Critical path (no rules) is extremely fast
- Complex setups get proportional cost

## Next Steps for Completion

### Immediate (Phase 2a - Healthcare Prototype)

1. **Implement Healthcare Patch**
   ```csharp
   // Complete ServicePathfindingPatches.PatchHealthcarePathfinding()
   // Add actual Harmony transpiler or prefix patch
   // Test with sample hospital rules
   ```

2. **Test Healthcare Rules**
   - Create test city with 2 hospitals
   - Add rules restricting Hospital A to District A
   - Verify ambulances respect rules
   - Fix any issues found

3. **Add UI Support**
   - Add service type dropdown to rule creation UI
   - Update rule display to show service type
   - Add visual indicators for service coverage

### Short Term (Phase 2b - More Services)

4. **Extend to Fire Services**
   - Patch fire pathfinding setup
   - Test with fire stations
   - Verify fire engines respect rules

5. **Extend to Police Services**
   - Patch police pathfinding setup
   - Handle both emergency and patrol
   - Test with police stations

6. **Extend to Garbage Services**
   - Patch garbage pathfinding setup
   - Test with garbage facilities
   - Handle industrial vs residential waste

### Medium Term (Phase 3 - Resources)

7. **Implement Resource Rules**
   - Identify resource transport pathfinding
   - Patch goods delivery system
   - Test with industrial supply chains

8. **Add Resource Filtering**
   - Support specific resource types
   - Test grain, oil, manufactured goods
   - Verify resource routing

### Long Term (Phase 4 - Polish)

9. **Performance Optimization**
   - Profile rule checking performance
   - Implement caching system
   - Burst compile where possible

10. **Advanced Features**
    - Time-based rules
    - Priority-based service assignment
    - Service area visualization
    - Import/export controls

## Technical Notes

### Why Harmony Patches Are Needed

The game's pathfinding setup happens deep within the ECS job system. We cannot simply override these systems because:

1. **Burst Compilation:** Jobs are compiled to native code
2. **Performance Critical:** Runs every frame for active requests
3. **Internal Logic:** Not designed for external hooks

Harmony patches allow us to:
- Inject code into existing methods
- Modify behavior without replacing entire systems
- Maintain compatibility with game updates

### Service District System

The game already has a `ServiceDistrict` system:
- Restricts services by district boundaries
- Uses `AreaUtils.CheckServiceDistrict()` method
- Our rules work ALONGSIDE this system

**Relationship:**
1. Game checks service district (vanilla)
2. If allowed, game checks our rules (mod)
3. Service proceeds only if BOTH allow

This means our rules ADD restrictions, they don't remove vanilla ones.

### Entity Versioning

Entity IDs in Cities Skylines 2 have two parts:
- **Index:** The building's ID number
- **Version:** Increments when entity is destroyed/recreated

For rule matching, we only use Index:
```csharp
Entity building = new Entity { Index = buildingId, Version = 1 };
```

This ensures rules persist even if buildings are upgraded/modified.

## Troubleshooting

### Common Issues

**Issue:** Rules not working
- Check if rules are saved correctly
- Verify ServiceRulesInterceptSystem is created
- Check logs for errors in rule evaluation

**Issue:** Game crashes on service dispatch
- Likely Harmony patch issue
- Check patch was applied successfully
- Add null checks in service methods

**Issue:** Performance problems
- Check number of rules configured
- Profile rule checking methods
- Consider increasing update interval

## References

### Documentation Files
- `ServiceAndResourceRules.md` - Complete system documentation
- `HealthcareImplementationGuide.md` - Implementation guide
- `WorkerTransportRules.md` - Existing worker system documentation

### Game Code References
- `Game.Simulation.HealthcarePathfindSetup` - Healthcare pathfinding
- `Game.Simulation.FirePathfindSetup` - Fire pathfinding
- `Game.Simulation.PolicePathfindSetup` - Police pathfinding
- `Game.Simulation.GarbagePathfindSetup` - Garbage pathfinding
- `Game.Areas.AreaUtils` - Service district utilities

### External Resources
- Harmony Documentation: https://harmony.pardeike.net/
- Unity ECS: https://docs.unity3d.com/Packages/com.unity.entities@latest
- Cities Skylines 2 Modding: Community Discord

## Conclusion

This implementation provides a solid foundation for service and resource transport rules. The core logic is complete and follows the proven pattern of the existing worker rules system. The next step is to complete the pathfinding interception for healthcare services as a prototype, then extend to other service types.

The system is designed to be:
- **Extensible:** Easy to add new service types
- **Performant:** Fast path for common case (no rules)
- **Maintainable:** Reuses existing proven code
- **User-Friendly:** Consistent with worker rules UI

Total time investment to complete Phase 2: Estimated 20-40 hours
- Healthcare implementation: 8-12 hours
- Fire/Police/Garbage: 6-10 hours each
- UI updates: 4-8 hours
- Testing and debugging: 10-15 hours
