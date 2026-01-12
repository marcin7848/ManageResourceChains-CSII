# Cities Skylines II - Transport & Pathfinding Analysis

## Key Findings

### 1. **Pathfinding System Components**

The game uses a sophisticated pathfinding system with these key elements:

#### **PathMethod enum** (Game.Pathfind.PathMethod)
Transport methods available:
- `Pedestrian` = 1
- `Road` = 2
- `Parking` = 4
- `PublicTransportDay` = 8
- `Track` = 16 (trains)
- `Taxi` = 32
- `CargoTransport` = 64
- `CargoLoading` = 128
- `Flying` = 256 (planes)
- `PublicTransportNight` = 512
- `Boarding` = 1024
- `Offroad` = 2048
- `SpecialParking` = 4096
- `MediumRoad` = 8192
- `Bicycle` = 16384
- `BicycleParking` = 32768

#### **RuleFlags enum** (Game.Pathfind.RuleFlags)
Controls what traffic is forbidden:
- `HasBlockage` = 1
- `ForbidCombustionEngines` = 2
- `ForbidTransitTraffic` = 4
- `ForbidHeavyTraffic` = 8
- `ForbidPrivateTraffic` = 16
- `ForbidSlowTraffic` = 32
- `AvoidBicycles` = 64

### 2. **Resource Transport System**

#### **StorageTransferRequest** (Game.Companies.StorageTransferRequest)
Used for transporting resources between buildings:
```csharp
public struct StorageTransferRequest : IBufferElementData
{
    public StorageTransferFlags m_Flags;  // Car, Transport, Track, Incoming
    public Resource m_Resource;           // What's being transported
    public int m_Amount;                  // How much
    public Entity m_Target;               // Where to/from
}
```

#### **StorageTransferFlags** (Game.Companies.StorageTransferFlags)
- `Car` = 1
- `Transport` = 2
- `Track` = 4
- `Incoming` = 8

### 3. **Worker Transport System**

#### **Worker component** (Game.Citizens.Worker)
```csharp
public struct Worker : IComponentData
{
    public Entity m_Workplace;           // Where they work
    public float m_LastCommuteTime;      // Travel time tracking
    public byte m_Level;                 // Education level
    public Workshift m_Shift;            // Work schedule
}
```

#### **TripNeeded** (Game.Citizens.TripNeeded)
Citizens queue trips when they need to travel to work, leisure, shopping, etc.

### 4. **How Pathfinding Works**

The pathfinding system uses these key steps:

1. **Setup Phase**: Systems like `CitizenPathfindSetup`, `ResourcePathfindSetup`, `GoodsDeliveryPathfindSetup` create pathfind requests
2. **Target Finding**: `PathfindTargetSeeker` finds valid destinations based on criteria
3. **Path Calculation**: `PathfindQueueSystem` calculates actual paths using weights and costs
4. **Result Application**: `PathfindResultSystem` applies the calculated path

### 5. **Interception Points**

To block/allow transport, you can intercept at these levels:

#### **Option A: Target Filtering (Recommended)**
Intercept during the target finding phase in systems like:
- `ResourcePathfindSetup.SetupResourceSellerJob` - for resource transport
- `CitizenPathfindSetup` - for worker commutes
- `GoodsDeliveryPathfindSetup` - for goods delivery

**Advantage**: Prevents paths from even being calculated, most efficient.

#### **Option B: Path Cost Modification**
Modify `PathfindParameters.m_MaxCost` or `PathfindWeights` to make certain paths extremely expensive.

**Advantage**: More flexible, allows gradual discouragement rather than hard blocks.

#### **Option C: Path Validation**
Check completed paths and reject them if they violate rules.

**Advantage**: Most compatible with other mods, least invasive.

## Implementation Strategy

### Recommended Approach: **Target Filtering with Rule System**

Create a system that runs BEFORE pathfinding setup and filters out disallowed targets:

```csharp
// Pseudo-code structure
public class ResourceChainFilterSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // For each pathfind request about to be created:
        // 1. Check if source/destination has rules
        // 2. Check rule type (Workers/Services/Resources)
        // 3. Check allow/disallow
        // 4. If disallowed, mark target as unavailable
        //    or set prohibitively high cost
    }
}
```

### Data Structure for Rules

You already have most of this in `ResourceChainConfig.cs`:

```csharp
public class ResourceChainRule
{
    public TransportType TransportType; // Workers/Services/Resources
    public ChainType Type;              // Incoming/Outgoing  
    public AllowType Allow;             // Allow/Disallow
    public List<int> Buildings;         // Affected buildings
}
```

**Key Addition Needed**: You need to track which specific resource types or worker levels to filter, not just all resources/workers.

---

## ✅ IMPLEMENTED: Worker Restriction System

### Overview

The mod now includes a fully functional **Worker Restriction System** that actively enforces rules preventing or allowing citizens to work at specific buildings based on configured rules using a whitelist/blacklist approach.

### Implementation Approach

**Post-Employment Validation with Active Enforcement**:
- Workers are hired normally through the game's standard pathfinding system
- A dedicated enforcement system (`ResourceChainPathfindSystem`) periodically checks all workers every ~2 seconds
- Workers violating rules are immediately removed from their workplace and become unemployed
- This approach is compatible with other mods and doesn't interfere with the game's core pathfinding

### How It Works

The `ResourceChainPathfindSystem` runs in the `GameSimulation` phase and:

1. **Queries all workers** in the game using ECS entity queries
2. **Gets each worker's home and workplace** by following the component relationships (Worker → HouseholdMember → PropertyRenter)
3. **Checks rules** to see if the worker-workplace combination is allowed using whitelist/blacklist logic
4. **Removes violating workers** from the workplace's employee list and removes their Worker component

### Rule Logic (Whitelist/Blacklist)

**DISALLOW = Blacklist** (Allow everything EXCEPT listed buildings):
- When you DISALLOW buildings, workers can go to ANY workplace EXCEPT the ones you picked
- Example: "Citizens can work anywhere except Factory A and Factory B"

**ALLOW = Whitelist** (Allow ONLY listed buildings):
- When you ALLOW buildings, workers can ONLY go to the buildings you picked
- Example: "Citizens can ONLY work at Office A and Office B, nowhere else"

### Rule Types

**OUTGOING Rules** (applied to residential buildings):
- Controls where residents of a building can work
- DISALLOW: Residents can work anywhere EXCEPT the listed workplaces (blacklist)
- ALLOW: Residents can ONLY work at the listed workplaces (whitelist)

**INCOMING Rules** (applied to workplace buildings):
- Controls who can work at a building
- DISALLOW: Workers from listed homes CANNOT work here (blacklist specific homes)
- ALLOW: ONLY workers from listed homes can work here (whitelist specific homes)

### Example Usage

**Scenario 1**: Prevent residents of Building A from working at Factory B and Factory C (Blacklist)
- Create an OUTGOING + DISALLOW + Workers rule on Building A
- Add Factory B and Factory C to the buildings list
- Result: Citizens from Building A can work ANYWHERE except Factory B and Factory C

**Scenario 2**: Restrict residents of Building A to ONLY work at Office X and Office Y (Whitelist)
- Create an OUTGOING + ALLOW + Workers rule on Building A
- Add Office X and Office Y to the buildings list
- Result: Citizens from Building A can ONLY work at Office X and Office Y, nowhere else

**Scenario 3**: Block specific residential areas from Office Building X (Blacklist)
- Create an INCOMING + DISALLOW + Workers rule on Office X
- Add the unwanted residential buildings to the list
- Result: Workers from those specific homes cannot work at Office X, but everyone else can

**Scenario 4**: Restrict Office Building X to ONLY workers from premium residential areas (Whitelist)
- Create an INCOMING + ALLOW + Workers rule on Office X
- Add the premium residential buildings to the list
- Result: ONLY workers from those specific homes can work at Office X

### Performance

- System updates every 128 frames (~2 seconds at 60fps)
- Uses efficient ECS queries for worker iteration
- Only processes when rules are configured
- Minimal performance impact on most cities
- Scales well with number of workers

### Data Storage

Rules are persisted per-building in JSON files:
- Location: `%LocalAppData%Low\Colossal Order\Cities Skylines II\ModsData\ManageResourceChains\`
- Filename format: `building_{entityId}.json`
- Contains building ID, rules array with type, allow/disallow, transport type, and target building lists

### Current Limitations

1. Workers are removed after being hired, not prevented from hiring initially
2. No filtering by education level or worker type
3. No time-based or conditional rules
4. District-based rules not yet implemented

### Future Enhancements

**Planned improvements**:
- Proactive prevention by hooking into job search system
- Education level filtering for more granular control
- Extension to resource deliveries and service vehicles
- Transport priority system implementation
- District-based rules instead of only building-based
- Time-based and conditional rules (day/night, seasonal, etc.)

---

## Future: Resource & Service Restrictions

The same enforcement pattern can be extended to other transport types:

**Resource Restrictions** (Planned):
- Block specific resource types from being transported between buildings
- Enforce resource chain priorities for industrial zones
- Control warehouse-to-factory supply chains

**Service Restrictions** (Planned):
- Block service vehicles (garbage, ambulance, fire, police) from specific buildings
- Control service coverage areas
- Restrict patrol and service routes

**Implementation Approach**:
- Reuse the `ResourceChainPathfindSystem` framework
- Add resource-type and service-type specific validation
- Implement both proactive filtering and reactive enforcement
- Maintain performance with comprehensive logging for debugging

