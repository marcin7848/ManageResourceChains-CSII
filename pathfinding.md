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
4. **Retrieves configurations** from the unified active configuration dictionary using entity ID and type
5. **Checks rules** in this order:
   - Building-level rules for home (OUTGOING)
   - Building-level rules for workplace (INCOMING)
   - District-level rules for home district (OUTGOING)
   - District-level rules for workplace district (INCOMING)
6. **Removes violating workers** from the workplace's employee list and removes their Worker component

**Configuration Access**:
```csharp
// Unified configuration access using compound keys
Dictionary<string, BuildingConfiguration> _activeConfigurations;

// Get building config: key = "entityId_Building"
var buildingKey = GetConfigKey(buildingEntityId, EntityType.Building);
if (_activeConfigurations.TryGetValue(buildingKey, out var buildingConfig))
{
    // Process building rules
}

// Get district config: key = "districtId_District"
var districtKey = GetConfigKey(districtEntityId, EntityType.District);
if (_activeConfigurations.TryGetValue(districtKey, out var districtConfig))
{
    // Process district rules
}
```

**Key Method**:
- `GetConfigKey(int entityId, EntityType type)` - Generates compound key for unified storage
- Returns: `"{entityId}_{type}"` format for dictionary lookups

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

**Unified Configuration System**:
All rules (both building and district) are persisted in a single unified JSON file:

- **Location**: `%LocalAppData%\Colossal Order\Cities Skylines II\ModsData\ManageResourceChains\`
- **Filename**: `entity_chain_configs.json`
- **Structure**: Single array containing all entity configurations with type discrimination

**Configuration Structure**:
```json
[
  {
    "BuildingEntityId": 12345,
    "Type": "Building",
    "Rules": [
      {
        "Id": "rule-guid-here",
        "Color": "#FF0000",
        "Type": "Outgoing",
        "Allow": "Disallow",
        "TransportType": "Workers",
        "Buildings": [67890, 11111],
        "Districts": [],
        "TransportPriorities": [],
        "SpecificResources": [],
        "WorkerEducationLevels": []
      }
    ]
  },
  {
    "BuildingEntityId": 67890,
    "Type": "District",
    "Rules": [...]
  }
]
```

**Key Fields**:
- `BuildingEntityId`: The entity ID (works for both buildings and districts despite the name)
- `Type`: Either `"Building"` or `"District"` - discriminates the entity type
- `Rules`: Array of rule configurations for this entity

**EntityType Enum**:
```csharp
public enum EntityType
{
    Building,
    District
}
```

**Benefits of Unified System**:
- Single file to back up/restore
- Easier to manage and debug
- Cleaner data structure
- No duplication between building and district handling code
- Easy to extend with new entity types (e.g., regions, neighborhoods)

### Configuration Management & Save Behavior

**Staging System**:
The mod uses a two-tier configuration system to prevent unintended changes to active gameplay:

1. **Active Configurations** (used by game logic):
   - These are the live configurations that control pathfinding and worker enforcement
   - Only updated when "Save all changes" is clicked
   - Stored in static dictionaries for game systems to access
   - Key format: `"entityId_Type"` (e.g., `"12345_Building"` or `"67890_District"`)

2. **Staging Configurations** (used by UI):
   - Temporary configurations for editing in the UI
   - All UI changes (add/remove/update rules, pick buildings) only affect staging
   - Never used by game logic until committed
   - Discarded if panel is closed without saving

**Save Workflow**:
1. User opens configuration panel → Staging config is initialized from active config (or empty if new)
2. User makes changes (add rules, pick buildings, etc.) → Changes applied only to staging
3. User clicks "Save all changes" → Staging configs are committed to active configs → Active configs saved to disk → Game logic now uses new rules
4. User closes without saving → Staging configs discarded → No changes persist

**Benefits**:
- Rules don't affect gameplay until explicitly saved
- Can experiment with rules without breaking active game
- Changes are cancellable (close panel without saving)
- Predictable behavior - game doesn't change unexpectedly during editing
- Clean separation between UI state and game state

### Current Limitations

1. Reactive-only enforcement (doesn't prevent job assignment)
2. No filtering by education level or worker type
3. No time-based or conditional rules
4. District detection relies on game's CurrentDistrict component

---

## How Rules Work: Buildings and Districts

### Understanding Rule Targets

When creating a rule, you can specify targets in two ways:
1. **Specific Buildings**: Individual buildings picked directly from the game world
2. **Districts**: All buildings within a district boundary

Both can be mixed in the same rule - you can have some specific buildings AND some districts.

### Rule Application Logic

#### Building-Level Rules

**What they control**: Rules created for a specific building (residential, commercial, or industrial).

**Picking Buildings**:
- Click the "+ Building" button in the manage panel
- Use the building picker to click buildings directly in the game world
- Each building is added individually to the rule
- You see the building entity ID in the list

**Picking Districts**:
- Click the "+ District" button in the manage panel
- Use the district picker to click districts directly in the game world
- All buildings within that district are affected by the rule
- You see the district entity ID in the list

**How it works**:
- When a worker/resource/service tries to go from/to this building, the rule checks:
  - Is the target building directly in my Buildings list? If yes → rule applies
  - Is the target building in one of my Districts? If yes → rule applies
  - If either condition is true, the rule takes effect

**Example**: Residential Building A with rule "OUTGOING DISALLOW WORKERS → Building X, District 1"
- Workers from Building A cannot go to Building X (specific building)
- Workers from Building A cannot go to ANY building in District 1
- Workers from Building A can go to buildings in other districts

#### District-Level Rules

**What they control**: Rules created for an entire district. All buildings in that district are affected.

**Picking Buildings**:
- Click the "+ Building" button in the district's manage panel
- Use the building picker to click specific buildings
- Rule applies to interactions between district buildings and those specific buildings

**Picking Districts**:
- Click the "+ District" button in the district's manage panel
- Use the district picker to click other districts
- Rule applies to interactions between buildings in your district and buildings in the target district

**How it works**:
- When any worker/resource/service tries to go from/to ANY building in this district, the rule checks:
  - Is the target building directly in my Buildings list? If yes → rule applies
  - Is the target building in one of my Districts? If yes → rule applies
  - All buildings in the district follow the same rule

**Example**: District 1 with rule "INCOMING DISALLOW WORKERS → District 2"
- Workers from ANY building in District 2 cannot enter ANY building in District 1
- This creates a district-wide barrier between the two districts

### Rule Direction and Type

#### OUTGOING Rules

**Building Context**: Controls where things can GO FROM this building.
- "OUTGOING DISALLOW WORKERS → District 1": Workers living here cannot work in District 1
- "OUTGOING ALLOW SERVICES → Building X": Services from here can only go to Building X

**District Context**: Controls where things can GO FROM buildings in this district.
- "OUTGOING DISALLOW RESOURCES → District 2": Resources cannot be sent from this district to District 2
- Applied to ALL buildings in the district

#### INCOMING Rules

**Building Context**: Controls where things can COME FROM to this building.
- "INCOMING DISALLOW WORKERS → District 3": Workers from District 3 cannot work here
- "INCOMING ALLOW RESOURCES → Building Y": Only accept resources from Building Y

**District Context**: Controls where things can COME FROM to buildings in this district.
- "INCOMING DISALLOW WORKERS → District 4": Workers from District 4 cannot work anywhere in this district
- Applied to ALL buildings in the district

### ALLOW vs DISALLOW

#### DISALLOW (Blacklist Mode)

**Behavior**: Block only the listed buildings/districts, allow everything else.

**Example**: "OUTGOING DISALLOW WORKERS → District 1"
- Workers CANNOT go to buildings in District 1
- Workers CAN go to all other districts
- Useful for creating exclusions

**Use cases**:
- "Don't send workers to the industrial district"
- "Don't accept resources from the polluted area"
- "Block service vehicles from this dangerous zone"

#### ALLOW (Whitelist Mode)

**Behavior**: Allow only the listed buildings/districts, block everything else.

**Example**: "OUTGOING ALLOW WORKERS → District 1, Building X"
- Workers CAN ONLY go to District 1 or Building X
- Workers CANNOT go anywhere else
- Useful for creating strict restrictions

**Use cases**:
- "Workers can only work in the downtown district"
- "Accept resources only from this specific factory"
- "Services can only come from the central district"

### Mixed Rules: Buildings + Districts

You can combine specific buildings and districts in the same rule for fine-grained control.

**Example**: "OUTGOING DISALLOW WORKERS → Building A, Building B, District 1, District 2"
- Workers cannot go to Building A
- Workers cannot go to Building B
- Workers cannot go to ANY building in District 1
- Workers cannot go to ANY building in District 2
- Workers can go anywhere else

**How checking works**:
1. Check if target is in Buildings list → If yes, rule applies
2. Check if target is in one of the Districts → If yes, rule applies
3. If either check passes, apply the ALLOW/DISALLOW logic

### Practical Examples

#### Example 1: Residential District Restricting Work Locations

**Setup**: District "Suburbs" with rule "OUTGOING DISALLOW WORKERS → District 'Industrial Zone'"

**Effect**:
- Residents of ANY building in Suburbs cannot work in ANY building in Industrial Zone
- They can work in other districts (Commercial, Downtown, etc.)
- This simulates zoning restrictions or commute preferences

#### Example 2: Factory Controlling Resource Sources

**Setup**: Building "Main Factory" with rule "INCOMING ALLOW RESOURCES → District 'Warehouse District', Building 'Special Supplier'"

**Effect**:
- Main Factory accepts resources ONLY from:
  - Buildings in Warehouse District
  - The specific Special Supplier building
- All other resource deliveries are blocked
- This creates a controlled supply chain

#### Example 3: District-to-District Barrier

**Setup**: 
- District 1: "OUTGOING DISALLOW WORKERS → District 2"
- District 2: "OUTGOING DISALLOW WORKERS → District 1"

**Effect**:
- Workers from District 1 cannot work in District 2
- Workers from District 2 cannot work in District 1
- Creates complete separation between two districts

#### Example 4: Mixed Targets for Granular Control

**Setup**: Building "Hospital" with rule "INCOMING ALLOW WORKERS → District 'Medical District', Building 'University', Building 'Research Center'"

**Effect**:
- Hospital accepts workers ONLY from:
  - ANY building in Medical District (trained medical professionals)
  - University building (researchers)
  - Research Center building (specialists)
- All other workers are blocked
- This simulates professional qualification requirements

### Rule Priority and Evaluation Order

**Evaluation order**:
1. Building-level rules are checked first
2. If no building-level rule applies, district-level rules are checked
3. First matching rule takes effect
4. If no rules match, transport is allowed

**Priority**:
- Building-specific rules override district-wide rules
- This allows exceptions: "District disallows X, but THIS building allows X"

**Example of priority**:
- District 1 has rule: "INCOMING DISALLOW WORKERS → District 2"
- Building A (in District 1) has rule: "INCOMING ALLOW WORKERS → District 2"
- Result: Most buildings in District 1 block District 2 workers, but Building A accepts them

### Transport Types

Rules can be created for three types of transport:

**WORKERS**: Citizens commuting to work
- Controls job access and worker pathfinding
- Most common use case for managing urban planning

**RESOURCES**: Industrial goods, materials, products
- Controls resource deliveries and supply chains
- Useful for managing industrial zones and trade

**SERVICES**: City services (garbage, healthcare, fire, police)
- Controls service vehicle access
- Useful for managing service coverage and emergency access

Each rule specifies ONE transport type. To control multiple types, create multiple rules.

### Best Practices

**Use Districts for Area-Wide Control**:
- Instead of clicking 50 individual buildings, click one district
- Much faster to set up
- Automatically includes new buildings built in the district

**Use Specific Buildings for Exceptions**:
- Add specific buildings to create exceptions to district rules
- Example: "Allow this factory in industrial district, disallow all others"

**Combine Buildings and Districts**:
- Start with districts for broad control
- Add specific buildings for edge cases
- Creates flexible, powerful rule sets

**Use DISALLOW for Simple Restrictions**:
- "Don't do X" is easier to manage than "Only do Y"
- Fewer items to maintain in the list

**Use ALLOW for Strict Control**:
- When you need tight restrictions
- When you want predictable, controlled behavior

---

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

---

## Public API for Game Systems

The ResourceChainManagementSystem provides a backward-compatible API for pathfinding and other game systems to access configurations:

**Get All Building Configurations**:
This method returns only configurations where the Type equals Building. It filters the unified dictionary to return building-only configurations, with the entity ID as the dictionary key.

**Get All District Configurations**:
This method returns only configurations where the Type equals District. It filters the unified dictionary to return district-only configurations, with the entity ID as the dictionary key.

**Get Specific Building Configuration**:
A static method that returns the configuration for a specific building entity. If no configuration is found, it returns an empty configuration object. This is used by pathfinding systems to check rules for individual buildings.

**Implementation Approach**:
The pathfinding system retrieves the management system instance from the World, then calls the appropriate getter method to obtain all building configurations for iteration. When checking rules for a specific building, it uses the static method to get that building's configuration directly.

**Note**: These methods provide a filtered view of the unified configuration system, maintaining compatibility with existing code while internally using the unified storage with EntityType discrimination. This design ensures that older code expecting separate building and district dictionaries continues to work without modifications.
