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

The mod now includes a fully functional **Worker Restriction System** that actively enforces rules preventing or allowing citizens to work at specific buildings based on configured rules using a whitelist/blacklist approach. The system supports both **building-level** and **district-level** rules.

### Implementation Approach

**Post-Employment Validation with Active Enforcement**:
- Workers are hired normally through the game's standard pathfinding system
- A dedicated enforcement system (`ResourceChainPathfindSystem`) periodically checks all workers every ~2 seconds (128 frames)
- Workers violating rules are immediately removed from their workplace and become unemployed
- This approach is compatible with other mods and doesn't interfere with the game's core pathfinding

### Building-Level vs District-Level Rules

#### Building-Level Rules (Higher Priority)
- Applied directly to specific buildings
- Rules configured on a building entity affect only that building
- Take precedence over district rules
- Most precise control
- Configured via the "Manage Resource Chains" button on individual buildings

#### District-Level Rules (Apply to All Buildings in District)
- Applied to all buildings within a district boundary
- Rules configured on a district entity affect every building in that district
- Used when you want to apply the same rules to an entire neighborhood
- More efficient for managing large areas
- Configured via the "Manage Resource Chains" button on district panels

#### Rule Priority Hierarchy

The system checks rules in the following order:

1. **Building-level rules are checked FIRST** (highest priority)
   - If a building has specific rules, they override district rules
   - Both OUTGOING (from home) and INCOMING (to workplace) rules are checked

2. **District-level rules are checked SECOND** (lower priority)
   - Only applied when a building has no specific rules
   - Both OUTGOING (from home district) and INCOMING (to workplace district) rules are checked

3. **Allow if no rules block it** (default behavior)
   - If neither building nor district has rules, transport is allowed

### How District Rules Work

#### District Assignment
- Buildings are automatically assigned to districts based on their geographical location
- The game's `CurrentDistrict` component on buildings tracks which district they belong to
- A building can only belong to one district at a time
- District boundaries are defined by the player using the Area Tool in-game

#### Rule Evaluation Process

When checking if a worker can travel from **Home A** to **Workplace B**:

**Step 1: Check Building-Level Rules**
1. Does Home A have OUTGOING worker rules? → Check them first
2. Does Workplace B have INCOMING worker rules? → Check them first
3. If any building-level rule blocks it → Transport is **DENIED**

**Step 2: Check District-Level Rules** (only if no building rules applied)
1. Does Home A belong to a district? → Get the district entity from `CurrentDistrict` component
2. Does that home district have OUTGOING worker rules? → Check them
3. Does Workplace B belong to a district? → Get the district entity
4. Does that workplace district have INCOMING worker rules? → Check them
5. If any district rule blocks it → Transport is **DENIED**

**Step 3: Allow if No Rules Block**
- If no rules triggered a block, the worker is allowed to work at that location

#### Technical Implementation

```csharp
// Pseudo-code showing the rule evaluation logic
Entity homeDistrict = GetBuildingDistrict(homeEntity, currentDistrictLookup);
Entity workplaceDistrict = GetBuildingDistrict(workplaceEntity, currentDistrictLookup);

// Priority 1: Building-level rules
foreach (building config with rules)
{
    if (config matches home and has OUTGOING rule)
        → Check if workplace is in the rule's building list
        → Apply ALLOW/DISALLOW logic
    
    if (config matches workplace and has INCOMING rule)
        → Check if home is in the rule's building list
        → Apply ALLOW/DISALLOW logic
}

// Priority 2: District-level rules (only if no building rules matched)
if (homeDistrict != null && district has OUTGOING rules)
    → Check if workplace is in the district rule's building list
    → Apply ALLOW/DISALLOW logic

if (workplaceDistrict != null && district has INCOMING rules)
    → Check if home is in the district rule's building list
    → Apply ALLOW/DISALLOW logic
```

#### Component Relationships

The system uses these ECS components to resolve district membership:

```
Building Entity
    └─ CurrentDistrict component
        └─ m_District field → District Entity

District Entity
    └─ District component
        └─ m_OptionMask (district settings)
```

**Key Methods:**
- `GetBuildingDistrict(Entity building, ComponentLookup<CurrentDistrict>)` - Returns the district entity for a building
- Uses `ComponentLookup<CurrentDistrict>` to efficiently query district membership
- Returns `Entity.Null` if building is not in any district

### Practical Examples

#### Example 1: District-Wide Factory Worker Ban (Blacklist)
**Setup:**
- District "Downtown Residential" has an OUTGOING DISALLOW rule for workers
- Rule lists Factory A, Factory B, and Factory C
- District contains 50 residential buildings

**Result:**
- All 50 residential buildings in Downtown will have workers blocked from the 3 factories
- Workers from Downtown can still work at offices, shops, and other workplaces
- You can override this for a specific building by adding building-level ALLOW rules

**Use Case:** Keep industrial pollution away from your premium residential district

#### Example 2: Exclusive Industrial Zone (Whitelist)
**Setup:**
- District "Industrial Zone" has an INCOMING ALLOW rule for workers
- Rule lists only the residential buildings within the industrial zone itself

**Result:**
- Factories in Industrial Zone can ONLY hire workers from local residences
- Workers from outside the district cannot commute to these factories
- This creates a self-contained industrial neighborhood

**Use Case:** Reduce traffic by creating self-sufficient industrial zones

#### Example 3: Mixed Rules (Building Overrides District)
**Setup:**
- District "Suburbs" has an OUTGOING DISALLOW rule blocking Downtown offices
- One specific apartment building (Building X) in Suburbs has an OUTGOING ALLOW rule permitting only Downtown offices

**Result:**
- Most suburban residents cannot work Downtown (district rule)
- Building X residents can ONLY work Downtown (building rule overrides district)
- This creates an exception for one upscale apartment with Downtown-commuting residents

**Use Case:** Fine-grained control with district-wide defaults and specific exceptions


### How It Works

**Building-Level Rules** (Higher Priority):
- Applied directly to specific buildings
- Rules configured on a building entity affect only that building
- Take precedence over district rules
- Most precise control

**District-Level Rules** (Apply to All Buildings in District):
- Applied to all buildings within a district boundary
- Rules configured on a district entity affect every building in that district
- Used when you want to apply the same rules to an entire neighborhood
- More efficient for managing large areas

**Rule Priority**:
1. Building-level rules are checked FIRST
2. If a building has specific rules, they override district rules
3. District rules apply when a building has no specific rules
4. If neither building nor district has rules, transport is allowed

### How District Rules Work

When checking if a worker can travel from Home A to Workplace B:

1. **Check Building-Level Rules**:
   - Does Home A have OUTGOING rules? Check them first
   - Does Workplace B have INCOMING rules? Check them first
   - If any building-level rule blocks it, transport is denied

2. **Check District-Level Rules** (if no building rules applied):
   - Does Home A belong to a district? Get the district entity
   - Does that district have OUTGOING worker rules? Check them
   - Does Workplace B belong to a district? Get the district entity
   - Does that district have INCOMING worker rules? Check them
   - If any district rule blocks it, transport is denied

3. **Allow if no rules block it**

**Example**: 
- District "Downtown" has an OUTGOING DISALLOW rule for Factory Zone buildings
- All residential buildings in Downtown will have workers blocked from Factory Zone
- You can override this for a specific building by adding building-level rules to that building

### How It Works

The `ResourceChainPathfindSystem` runs in the `GameSimulation` phase and:

1. **Queries all workers** in the game using ECS entity queries
2. **Gets each worker's home and workplace** by following the component relationships (Worker → HouseholdMember → PropertyRenter)
3. **Checks if buildings belong to districts** using the `CurrentDistrict` component
4. **Checks rules** in this order:
   - Building-level rules for home (OUTGOING)
   - Building-level rules for workplace (INCOMING)
   - District-level rules for home district (OUTGOING)
   - District-level rules for workplace district (INCOMING)
5. **Removes violating workers** from the workplace's employee list and removes their Worker component

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

### District-Level Rules

**Overview**:
District-level rules apply to ALL buildings within a district automatically, providing broader control without managing individual buildings.

**How Districts Work**:
- Buildings are assigned to districts based on the game's `CurrentDistrict` component
- The system automatically detects which district a building belongs to
- District rules complement building-level rules (building rules take priority)

**District Rule Types**:

**OUTGOING District Rules** (applied to residential districts):
- Controls where ALL residents of the district can work
- DISALLOW: District residents can work anywhere EXCEPT the listed workplaces (blacklist)
- ALLOW: District residents can ONLY work at the listed workplaces (whitelist)

**INCOMING District Rules** (applied to commercial/industrial districts):
- Controls who can work at buildings in the district
- DISALLOW: Workers from listed buildings CANNOT work in this district (blacklist)
- ALLOW: ONLY workers from listed buildings can work in this district (whitelist)

**Priority System**:
1. **Building-specific rules** are checked FIRST and take highest priority
2. **District-level rules** are checked SECOND as a fallback
3. If no rules apply, transport is allowed by default

**District Example Usage**:

**Scenario 1**: Prevent entire residential district from working at industrial factories
- Create an OUTGOING + DISALLOW + Workers rule on residential district
- Add all industrial factory buildings to the list
- Result: All residents in the district cannot work at those factories

**Scenario 2**: Restrict premium office district to only high-end residential areas
- Create an INCOMING + ALLOW + Workers rule on office district
- Add the premium residential buildings to the allowed list
- Result: Only workers from those specific homes can work at ANY building in the office district

### Performance

- System updates every 128 frames (~2 seconds at 60fps)
- Uses efficient ECS queries for worker iteration
- Only processes when rules are configured
- Minimal performance impact on most cities
- Scales well with number of workers
- District lookups are optimized with ComponentLookup

### Data Storage

**Building Rules** are persisted in JSON files:
- Location: `%LocalAppData%Low\Colossal Order\Cities Skylines II\ModsData\ManageResourceChains\`
- Filename format: `building_{entityId}.json`
- Contains building ID, rules array with type, allow/disallow, transport type, and target building lists

**District Rules** are persisted in JSON files:
- Location: `%LocalAppData%Low\Colossal Order\Cities Skylines II\ModsData\ManageResourceChains\`
- Filename format: `district_{entityId}.json`
- Contains district ID, rules array with same structure as building rules

### Current Limitations

1. Workers are removed after being hired, not prevented from hiring initially
2. No filtering by education level or worker type
3. No time-based or conditional rules
4. District detection relies on game's CurrentDistrict component

### Future Enhancements

**Planned improvements**:
- Proactive prevention by hooking into job search system
- Education level filtering for more granular control
- Extension to resource deliveries and service vehicles
- Transport priority system implementation
- Time-based and conditional rules (day/night, seasonal, etc.)
- Visual district overlay showing active rules

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

