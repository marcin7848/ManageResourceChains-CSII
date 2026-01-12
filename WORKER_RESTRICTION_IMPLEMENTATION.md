# Worker Restriction System Implementation

## Overview
The worker restriction system actively enforces rules that prevent citizens from working at specific buildings based on configured rules.

## How It Works

### 1. Rule Configuration
- Users can create rules for buildings with the following properties:
  - **Type**: `Outgoing` (from home) or `Incoming` (to workplace)
  - **Allow**: `Allow` or `Disallow`
  - **Transport Type**: `Workers` (for worker restrictions)
  - **Buildings**: List of building IDs that are affected by the rule

### 2. Worker Enforcement System (`ResourceChainPathfindSystem`)

The system runs every 128 frames (~2 seconds at 60fps) and performs the following:

#### A. Query All Workers
- Retrieves all citizens with the `Worker` component
- Gets their home building (via `HouseholdMember` → `Household` → `PropertyRenter`)
- Gets their workplace from the `Worker` component

#### B. Check Rules
For each worker, the system checks two scenarios:

**OUTGOING Rules (from Home)**:
- If the worker's HOME building has an OUTGOING + DISALLOW rule
- And the WORKPLACE is in that rule's building list
- Then the worker is BLOCKED

**INCOMING Rules (to Workplace)**:
- If the WORKPLACE has an INCOMING + DISALLOW rule
- And the worker's HOME is in that rule's building list
- Then the worker is BLOCKED

#### C. Remove Violating Workers
When a worker violates a rule:
1. Log the restriction: `🚫 [WORKER RESTRICTION] Removing worker X from workplace Y. Home: Z`
2. Remove the worker from the workplace's Employee buffer
3. Remove the Worker component from the citizen (makes them unemployed)
4. Log summary: `✅ [WORKER ENFORCEMENT] Removed N workers from disallowed workplaces`

### 3. Example Scenarios

#### Scenario 1: Prevent Residents from Working at a Factory
**Setup**:
- Home Building: Residential Building A (ID: 114846)
- Workplace: Factory B (ID: 85704)
- Rule on Building A:
  - Type: OUTGOING
  - Allow: DISALLOW
  - Transport Type: Workers
  - Buildings: [85704]  ← Factory B

**Result**: Citizens living in Building A cannot work at Factory B. If they're already working there, they'll be removed and become unemployed.

#### Scenario 2: Prevent Specific Residents from Entering a Workplace
**Setup**:
- Workplace: Office Building X (ID: 203881)
- Unwanted Homes: Buildings [114846, 114849, 230680]
- Rule on Office X:
  - Type: INCOMING
  - Allow: DISALLOW
  - Transport Type: Workers
  - Buildings: [114846, 114849, 230680]

**Result**: Workers from those specific residential buildings cannot work at Office X.

## Logging

The system provides detailed logs to help debug worker restrictions:

```
🚫 Worker transport BLOCKED: Home 114846 -> Workplace 85704 (OUTGOING DISALLOW rule 'abc123')
🚫 [WORKER RESTRICTION] Removing worker 12345 from workplace 85704. Home: 114846
  ✓ Removed from employee list at index 2
  ✓ Worker component removed, citizen is now unemployed
✅ [WORKER ENFORCEMENT] Removed 1 workers from disallowed workplaces
```

## Technical Implementation

### Key Components
1. **ResourceChainPathfindSystem** - Main enforcement system
2. **Entity Queries** - Queries all workers efficiently
3. **Component Lookups** - Fast access to Worker, HouseholdMember, PropertyRenter, Employee components
4. **EntityCommandBuffer** - Deferred removal of Worker components

### Performance
- Update interval: 128 frames (~2 seconds)
- Uses ECS queries for efficient worker iteration
- Only processes workers that violate rules
- Logs are only generated when workers are actually removed

## Future Enhancements
1. **Prevention**: Hook into FindJobSystem to prevent assignment in the first place
2. **Resource/Service Restrictions**: Extend to block resource deliveries and services
3. **Education Levels**: Filter by worker education levels
4. **Dynamic Rules**: Support time-based or conditional rules
5. **UI Feedback**: Show blocked workers in the UI

## Testing Checklist
✅ Build a residential building
✅ Wait for citizens to move in and find jobs
✅ Create an OUTGOING + DISALLOW rule on the residential building
✅ Add target workplaces to the rule
✅ Save the rule
✅ Check logs for worker removal messages
✅ Verify citizens become unemployed
✅ Verify they don't return to disallowed workplaces

## Notes
- Workers are removed, not just prevented from going to work
- They become unemployed and will seek new jobs elsewhere
- The system respects both OUTGOING and INCOMING rules
- Rules are checked every ~2 seconds, not instantly

