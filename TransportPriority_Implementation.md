# Transport Priorities Implementation Documentation

## Overview
This document outlines the research and proposed implementation for the "Transport Priorities" feature in the Manage Resource Chains mod. The goal is to allow players to select specific transport lines, stops, or stations and prioritize their use for workers, resources, or services.

## 1. Selectable Transport Entities

The Cities: Skylines II engine represents transport infrastructure through several entity types. For this mod, we can support picking:

| Entity Type | Game Component | Description |
| :--- | :--- | :--- |
| **Transport Line** | `Game.Routes.TransportLine` | An entire route (e.g., "Bus Line 1"). Picking this prioritizes the whole line. |
| **Transport Stop** | `Game.Routes.TransportStop` | A specific point where vehicles stop. Best for granular control. |
| **Transport Station** | `Game.Prefabs.TransportStation` | A building (e.g., Central Train Station) that contains one or more stops. |
| **Cargo Terminal** | `Game.Prefabs.CargoTransportStation` | Specialized buildings for resource transport (Cargo Trains, Ships, etc.). |
| **Taxi Stand** | `Game.Routes.TaxiStand` | Specific locations for taxi pickup/drop-off. |

### Recommended Picking Strategy
- **Stations/Terminals**: Players pick the building. The mod then finds all stops associated with that building (via `Owner` or `Attachment` components).
- **Lines**: Players pick a line from the transport overview or by clicking a vehicle/stop belonging to it.
- **Stops**: Players click the stop marker in the world.

## 2. Transport Ways and Groups

### Workers (Passenger Transport)
Workers use transport modes that support passengers.
- **Modes**: Bus, Train, Tram, Subway, Ferry, Airplane, Helicopter, Taxi.
- **Key Components**: `PublicTransportStationData`, `TransportLineData.m_PassengerTransport = true`.

### Resources (Cargo Transport)
Resources use transport modes that support cargo.
- **Modes**: Cargo Train, Cargo Ship, Cargo Airplane.
- **Key Components**: `CargoTransportStationData`, `TransportLineData.m_CargoTransport = true`.

### Services
Services mostly use road networks, but some specific services use specialized transport.
- **Modes**: Roads (default), Helicopter (Police/Fire/Medical).
- **Note**: "Prioritizing" services usually means prioritizing the road segments they use, but for this mod, it could apply to specialized service terminals.

## 3. Pathfinding Influence Mechanisms

To "force" or prioritize a transport way, we must influence the game's pathfinding cost calculation. The pathfinder (A*) uses `PathfindWeights` consisting of:
- **Time**: Travel time + waiting time.
- **Money**: Ticket prices or fees.
- **Comfort**: Penalty for uncomfortable stops/vehicles.
- **Behaviour**: Internal game logic weights.

### How to Decrease Cost (Increase Priority)

#### A. Modify Ticket Price
- Component: `Game.Routes.TransportLine` -> `m_TicketPrice`.
- Logic: Setting this to 0 or a very low value makes the line more attractive to workers/passengers.

#### B. Modify Comfort Factor
- Component: `Game.Routes.TransportStop` -> `m_ComfortFactor` (0.0 to 1.0).
- Logic: Increasing this value reduces the comfort penalty in `PathUtils.GetTransportStopSpecification`. A value of 1.0 removes the penalty entirely.

#### C. Modify Loading Factor
- Component: `Game.Routes.TransportStop` -> `m_LoadingFactor`.
- Logic: Influences `GetStopDuration`. Higher loading factors can slightly reduce stop time.

#### D. Route Modifiers (Policies)
- Component: `Game.Routes.RouteModifier` buffer on the line entity.
- Logic: Can be used to apply `VehicleInterval` modifiers, effectively reducing waiting time.

## 4. Implementation Feasibility

### Global Priority (High Feasibility)
Modifying the components on the transport entities themselves.
- **Pros**: Relatively easy to implement by updating ECS components.
- **Cons**: Affects EVERYONE in the city, not just the specific workers/resources targeted by a `ResourceChainRule`.

### Per-Rule (Targeted) Priority (Low Feasibility)
Making a line cheap only for specific workers from Building A.
- **Pros**: Perfectly matches the mod's "Rule" philosophy.
- **Cons**: Extremely difficult. The pathfinder is global and Burst-compiled. There is no native support for "per-request weights" for specific transport lines.
- **Possible Workaround**: Intercept `CitizenPathfindSetup` and modify global weights for the whole path, but this still doesn't target a specific line unless we can somehow "tag" the line in the pathfinder.

## 5. Proposed Implementation Plan

### Stage 1: UI & Data Structure
- Expand `TransportStationType` to include all modes (Tram, Bus, Cargo, etc.).
- Update `TransportPriority` interface to support either `stationEntity` or `lineEntity`.
- Implement a "Picker" tool that can select Buildings (Stations) and Routes (Lines).

### Stage 2: Global Component Modification
- Create a `TransportPrioritySystem` that monitors `ResourceChainRule` changes.
- When a line/stop is prioritized, update its `m_TicketPrice` and `m_ComfortFactor` in the ECS.
- Revert changes when the priority is removed.

### Stage 3: "Forcing" Logic
- Since "hard forcing" is not natively supported, implement "soft forcing" by setting the cost to the absolute minimum allowed by the engine.
- For resources, prioritize `CargoTransportStation` by increasing its `m_LoadingFactor`.

## 6. Necessary Game DLL Classes to Inspect
- `Game.Pathfind.PathUtils`: For cost calculation logic.
- `Game.Routes.RouteUtils`: For route data handling.
- `Game.Simulation.TransportLineSystem`: For how lines are updated.
- `Game.Prefabs.TransportLinePrefab`: For default values.

### Feasibility Analysis of Forced Transport Routing

Based on my research into the Cities: Skylines II pathfinding and simulation systems, I can confirm that **both scenarios you described are possible to implement**, though they require different technical approaches.

---

### Scenario 1: Forced Routing for Workers
**Goal**: Force workers from Building A to use a specific Tram Station and Bus Stop on their way to their workplace.

#### Implementation Approach: **Multi-Stage Trip State Machine**
The game's pathfinder is "selfish" and always looks for the cheapest path (time/money/comfort). It does not natively support "via" waypoints for citizens. To implement this, we would use a **multi-stage trip system**:

1.  **Trip Interception**: When a worker from Building A starts a commute to their workplace, the mod intercepts the pathfinding request in `CitizenPathfindSetup`.
2.  **Destination Redirection**: Instead of requesting a path directly to the workplace, the mod changes the destination to the **first prioritized stop** (e.g., the Tram Station).
3.  **Intermediate State**: We attach a custom component (e.g., `ForcedTripState`) to the citizen to store their *actual* final destination (the workplace) and the list of remaining "via" points.
4.  **Arrival Detection**: When the worker arrives at the Tram Station, the mod detects this, clears the current trip, and triggers a new `TripNeeded` request to the next point (the Bus Stop) or the final workplace.
5.  **Sequential Forcing**: This process repeats until the worker reaches their workplace.

**Feasibility**: ✅ **High**. It effectively "tricks" the AI into taking multiple short trips that, combined, form the path you want.

---

### Scenario 2: Forced Routing for Resources
**Goal**: Force resources from a producer building to go through a specific Cargo Train Station before reaching the destination building.

#### Implementation Approach: **Storage Transfer Redirection**
Resource transport in CS2 is managed via `StorageTransferRequest` elements.

1.  **Request Interception**: The mod monitors `StorageTransferRequest` buffers in the `ResourcePathfindSetup` system.
2.  **Target Modification**: If a request matches your rule (Producer A -> Consumer B), the mod changes the `m_Target` of the request from Consumer B to **Cargo Train Station C**.
3.  **Cargo Handover**: The resource is delivered to the Cargo Station normally.
4.  **Secondary Dispatch**: Once the resource is at the Cargo Station, the existing rule system of the mod can be used to ensure the Cargo Station prioritizes sending those resources to Consumer B (using an **OUTGOING** rule on the Cargo Station).

**Feasibility**: ✅ **Very High**. The game's economy system is already designed around intermediate storage (warehouses and terminals), so redirecting the "target" of a delivery is a native operation.

---

### Summary Table

| Feature | Scenario 1: Workers | Scenario 2: Resources |
| :--- | :--- | :--- |
| **Possibility** | Yes | Yes |
| **Complexity** | Medium (Requires trip state tracking) | Low (Redirecting transfer targets) |
| **Behavior** | Home → Station → Stop → Work | Producer → Cargo Terminal → Consumer |
| **Native Support** | No (requires mod logic) | Partial (native storage transfers) |

### Conclusion
Both scenarios are definitely possible. The implementation would involve creating a new system that hooks into the **Pathfind Setup** phase to modify the targets of citizens and resources before the actual pathfinding begins. This ensures that the game "thinks" it's just doing a normal trip to the intermediate station you've chosen.