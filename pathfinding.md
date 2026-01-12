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

