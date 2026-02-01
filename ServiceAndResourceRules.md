# Service and Resource Rules System - Complete Documentation

## Overview

The ManageResourceChains mod extends the worker transport rules system to support three types of transport restrictions:

1. **Workers** - Citizens traveling from residential buildings to workplaces
2. **Services** - Emergency and city service vehicles responding to buildings
3. **Resources** - Materials and goods transported between industrial/commercial buildings

## Transport Types

### 1. Workers (Already Implemented)
Controls which citizens can work where based on their home building.

**How it works:**
- Rules are checked during pathfinding when citizens look for jobs
- Worker restrictions are enforced by removing existing workers who violate rules
- Uses the `CheckWorkerRestrictionsJob` to periodically validate all employed citizens

**Example Use Cases:**
- Allow workers from District A to work only in District B
- Prevent workers from specific residential buildings from working at certain facilities
- Create exclusive employment zones

### 2. Services (To Be Implemented)
Controls which service vehicles can respond to which buildings.

#### Service Types:

| Service | Buildings | Vehicles | Request Type |
|---------|-----------|----------|--------------|
| **Healthcare** | Hospital | Ambulance, Medical Helicopter | `HealthcareRequest` |
| **Deathcare** | Cemetery, Crematorium | Hearse | `HealthcareRequest` (death) |
| **Fire & Rescue** | Fire Station | Fire Engine, Fire Helicopter | `FireRescueRequest` |
| **Police** | Police Station | Police Car, Police Helicopter | `PoliceEmergencyRequest`, `PolicePatrolRequest` |
| **Garbage** | Garbage Facility, Landfill, Incinerator | Garbage Truck | `GarbageCollectionRequest` |
| **Post** | Post Office | Post Van | `PostVanRequest` |
| **Disaster/Evacuation** | Emergency Shelter | Evacuation buses | `EvacuationRequest` |
| **Maintenance** | Maintenance Depot | Maintenance Vehicle | `MaintenanceRequest` |

**How it works:**
- Services use pathfinding setup systems (e.g., `HealthcarePathfindSetup`, `FirePathfindSetup`)
- Each service checks `AreaUtils.CheckServiceDistrict()` to determine if a building is within service range
- Rules should be checked during pathfinding setup, before vehicles are dispatched
- The system uses `ServiceDispatch` buffers to queue service requests

**Key Components:**
- **Request Entities**: Store information about service needs (e.g., `HealthcareRequest`)
- **Dispatch Systems**: Match service vehicles to requests (e.g., `HealthcareDispatchSystem`)
- **Pathfind Setup**: Configure pathfinding for service vehicles (e.g., `HealthcarePathfindSetup`)
- **AI Systems**: Control vehicle behavior (e.g., `AmbulanceAISystem`)

**Example Use Cases:**
- Assign specific fire stations to specific neighborhoods
- Create exclusive ambulance coverage zones for hospitals
- Prevent police from certain stations from patrolling specific districts
- Route garbage collection to specific facilities based on building type

### 3. Resources (To Be Implemented)
Controls which resources can be transported between buildings.

#### Resource Types:

| Category | Resources | Buildings |
|----------|-----------|-----------|
| **Raw Materials** | Grain, Livestock, Vegetables, Cotton, Wood, Stone, Coal, Oil, Ore | Extractors, Farms |
| **Processed Goods** | Food, Textiles, Paper, Metals, Plastics, Electronics, Furniture, Vehicles | Processing Plants, Factories |
| **Commercial Goods** | Various retail goods | Commercial buildings |
| **Utilities** | Electricity, Water | Power plants, Water facilities |

**How it works:**
- Resources use `ResourcePathfindSetup` and cargo/delivery pathfinding
- Transport happens through delivery trucks and cargo vehicles
- Requests are managed through `GoodsDeliveryRequest` and resource buyer/seller systems
- The `ResourceFlowSystem` manages resource distribution

**Key Components:**
- **Resource Producers**: Buildings that create resources (`ResourceProducer` component)
- **Resource Consumers**: Buildings that need resources (`ResourceConsumer` component)
- **Delivery Systems**: Handle transport (e.g., `DeliveryTruckAISystem`, `GoodsDeliveryDispatchSystem`)
- **Storage**: Warehouses and storage facilities (`StorageProperty`)

**Example Use Cases:**
- Force factories to get resources from specific suppliers
- Prevent certain industrial buildings from delivering to specific zones
- Create dedicated supply chains for different districts
- Route specific resource types through designated buildings

## Implementation Architecture

### Pathfinding Interception

All three transport types use the pathfinding system, which we can intercept at the setup phase:

```
1. Entity needs transport (worker/service/resource)
2. PathfindSetupSystem creates pathfinding request
3. **[INTERCEPT HERE]** - Check rules before pathfinding
4. Pathfinding occurs (if allowed)
5. Entity travels to destination
```

### Rule Evaluation Flow

```
For each transport request:
  1. Identify source and destination buildings
  2. Get all rules for source building
  3. Filter by TransportType (Workers/Services/Resources)
  4. Filter by direction (Incoming/Outgoing)
  5. Remove district rules if building rules exist
  6. Evaluate:
     - If any DISALLOW rule matches → BLOCK
     - If ALLOW rules exist but none match → BLOCK
     - Otherwise → ALLOW
```

### Service-Specific Implementation

Each service type requires slightly different handling:

#### Healthcare/Deathcare
- **Intercept**: `HealthcarePathfindSetup.SetupAmbulancesJob`
- **Check**: When ambulance/hearse pathfinding is set up
- **Target**: Buildings where citizens need healthcare
- **Rule Filter**: `TransportType.Services` + service subtype (healthcare/deathcare)

#### Fire & Rescue
- **Intercept**: `FirePathfindSetup.SetupFireEnginesJob`
- **Check**: When fire engine pathfinding is set up
- **Target**: Buildings on fire or needing rescue
- **Rule Filter**: `TransportType.Services` + service subtype (fire)

#### Police
- **Intercept**: `PolicePathfindSetup.SetupPolicePatrolsJob`
- **Check**: When police vehicle pathfinding is set up for patrol or emergency
- **Target**: Buildings needing police (high crime) or patrol routes
- **Rule Filter**: `TransportType.Services` + service subtype (police)
- **Special**: Supports both emergency and patrol purposes

#### Garbage
- **Intercept**: `GarbagePathfindSetup.SetupGarbageCollectorsJob`
- **Check**: When garbage truck pathfinding is set up
- **Target**: Buildings with garbage to collect
- **Rule Filter**: `TransportType.Services` + service subtype (garbage)
- **Special**: Can distinguish industrial vs residential waste

#### Post
- **Intercept**: `PostServicePathfindSetup` (in post van systems)
- **Check**: When post van pathfinding is set up
- **Target**: Buildings with mail to deliver/collect
- **Rule Filter**: `TransportType.Services` + service subtype (post)

### Resource-Specific Implementation

#### Industry Resources
- **Intercept**: `ResourcePathfindSetup` and goods delivery setup
- **Check**: When delivery trucks plan routes
- **Target**: Destination buildings for resource delivery
- **Rule Filter**: `TransportType.Resources` + resource type
- **Special**: Can filter by specific resource types (grain, oil, etc.)

## Data Structure Extensions

### Enhanced ResourceChainRule

```csharp
public class ResourceChainRule
{
    // Existing fields
    public ChainType Type { get; set; }
    public AllowType Allow { get; set; }
    public TransportType TransportType { get; set; }
    
    // NEW: Service-specific filter
    public ServiceType? ServiceSubtype { get; set; } = null;
    
    // NEW: Resource-specific filter
    public ResourceType? ResourceSubtype { get; set; } = null;
    public List<int> SpecificResourceIds { get; set; } = new List<int>();
}

public enum ServiceType
{
    Healthcare,
    Deathcare,
    Fire,
    Police,
    Garbage,
    Post,
    Evacuation,
    Maintenance
}

public enum ResourceType
{
    RawMaterials,
    ProcessedGoods,
    CommercialGoods,
    All
}
```

## Pathfinding System Integration

### Method 1: Custom Pathfinding Setup System (Recommended)

Create a system that runs before the game's pathfinding setup systems:

```csharp
[UpdateBefore(typeof(PathfindSetupSystem))]
public partial class ServiceRulesPathfindSetup : GameSystemBase
{
    // Intercepts service pathfinding setup
    // Modifies SetupData before pathfinding occurs
}
```

### Method 2: Harmony Patches (Alternative)

Patch the specific pathfinding setup jobs:
- `HealthcarePathfindSetup.SetupAmbulancesJob.Execute`
- `FirePathfindSetup.SetupFireEnginesJob.Execute`
- `PolicePathfindSetup.SetupPolicePatrolsJob.Execute`
- etc.

### Method 3: Hybrid Approach (Best)

Combine both:
1. Use custom system for rule evaluation
2. Use lightweight patches to inject rule checks at critical points
3. Store evaluated results in components for quick lookup

## Implementation Priority

### Phase 1: Healthcare Services (Easiest to Test)
- Implement healthcare service rules
- Test with hospitals and ambulances
- Verify rules prevent ambulances from serving restricted buildings

### Phase 2: Fire & Police Services
- Add fire service rules
- Add police service rules
- Handle both emergency and patrol scenarios

### Phase 3: Garbage & Utilities
- Implement garbage collection rules
- Add post service rules
- Test with various facility types

### Phase 4: Resource Transport
- Implement resource delivery rules
- Support specific resource type filtering
- Test with industrial supply chains

## Testing Scenarios

### Healthcare Example
```
Setup:
- Hospital A in District North
- Hospital B in District South
- Residential buildings in both districts

Rule: Hospital A → ALLOW only buildings in District North
Expected: Only citizens in District North can be served by Hospital A ambulances

Rule: Hospital B → DISALLOW buildings in District North
Expected: Hospital B ambulances won't respond to District North emergencies
```

### Fire Service Example
```
Setup:
- Fire Station A near industrial zone
- Fire Station B in residential area
- Buildings in both zones

Rule: Fire Station A → ALLOW only industrial buildings
Expected: Fire Station A only responds to industrial fires

Rule: Fire Station B → DISALLOW industrial zone
Expected: Fire Station B ignores industrial fires
```

### Resource Example
```
Setup:
- Farm A producing grain
- Farm B producing grain
- Bakery needing grain

Rule: Bakery → ALLOW INCOMING only from Farm A
Expected: Bakery only accepts grain deliveries from Farm A

Rule: Farm B → DISALLOW OUTGOING to Bakery
Expected: Farm B won't deliver to Bakery (same result, different approach)
```

## Technical Notes

### Service Districts
The game has a built-in `ServiceDistrict` system that already restricts services by district. Our rules system should:
1. Respect existing service district restrictions (don't override them)
2. Add more granular building-level restrictions
3. Work alongside the built-in system

### Performance Considerations
- Service pathfinding happens frequently (every frame for active requests)
- Rule evaluation must be very fast (use cached NativeHashMaps)
- Consider caching rule results for active requests
- Use Burst compilation for all rule checking code

### Edge Cases
1. **Multiple Rules Conflict**: Building rules override district rules
2. **No Rules**: Default to allow (maintains vanilla behavior)
3. **Partial Coverage**: Some services allowed, others blocked
4. **Outside Connections**: Handle import/export services specially
5. **Service District + Custom Rules**: Both must allow for service to proceed

## Next Steps for Implementation

1. Extend `TransportType` enum with service subtypes
2. Create service-specific rule checking methods
3. Implement pathfinding interception for healthcare (prototype)
4. Test healthcare rules thoroughly
5. Expand to other service types
6. Add UI controls for service-specific rules
7. Implement resource transport rules
8. Add filtering by specific resource types
9. Performance optimization and testing
10. Documentation and user guide

## Code Integration Points

### Files to Modify
- `ResourceChainConfig.cs` - Add service/resource enums
- `ResourceChainRulesSystem.cs` - Add service checking logic
- Create new `ServiceRulesPathfindSetup.cs` - Intercept pathfinding
- Update UI files to support new rule types

### Files to Reference
- `Game.Simulation.HealthcarePathfindSetup` - Healthcare pathfinding logic
- `Game.Simulation.FirePathfindSetup` - Fire service pathfinding logic
- `Game.Simulation.PolicePathfindSetup` - Police pathfinding logic
- `Game.Simulation.GarbagePathfindSetup` - Garbage collection logic
- `Game.Simulation.ResourcePathfindSetup` - Resource transport logic
- `Game.Areas.AreaUtils` - Service district checking methods
