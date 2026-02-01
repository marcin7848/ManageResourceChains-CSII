# Worker Transport Rules Documentation

## Overview

The Worker Transport Rules system controls whether workers (citizens) are allowed to travel from their home buildings to workplace buildings. This system evaluates rules configured for buildings and districts to determine if a worker can be employed at a specific location.

## Rule Evaluation Algorithm

The `IsWorkerTransportAllowed` method follows a 4-step algorithm to determine if a worker can travel from Building A (home) to Building B (workplace):

### Step 1: Collect All Relevant Rules

The system collects all worker transport rules from:
- **Home Building (A)**: All rules configured directly on the home building
- **Workplace Building (B)**: All rules configured directly on the workplace building
- **Home District**: All rules configured on the district that contains Building A
- **Workplace District**: All rules configured on the district that contains Building B

Each collected rule is marked with metadata indicating whether it comes from a building or a district.

**Result**: A list of all potentially relevant rules with source information.

### Step 2: Filter Rules by Direction

The system filters rules based on their direction type and origin:

- **OUTGOING rules from Home**: Keep only rules that:
  - Are OUTGOING type
  - Come from the home building (A) or its district

- **INCOMING rules to Workplace**: Keep only rules that:
  - Are INCOMING type
  - Come from the workplace building (B) or its district

This step ensures that only rules applying to the A→B direction are considered.

**Result**: A list of direction-filtered rules relevant to the worker's travel from A to B.

### Step 3: Prioritize Building Rules Over District Rules

Building-level rules always take priority over district-level rules:

- If ANY building rules exist in the filtered list, ALL district rules are removed
- If NO building rules exist, district rules remain and are evaluated

This ensures that specific building configurations override broader district policies.

**Result**: A final list of rules to evaluate, containing either building rules OR district rules, but not both.

### Step 4: Evaluate Remaining Rules

The system evaluates the final rule list using the following logic:

#### Rule Types

- **ALLOW rules** (Whitelist): Transport is allowed ONLY if the target is in the rule's building/district list
- **DISALLOW rules** (Blacklist): Transport is blocked if the target is in the rule's building/district list

#### Evaluation Logic

1. **If any DISALLOW rule matches**: Transport is BLOCKED
   - Example: Home has OUTGOING DISALLOW rule that includes the workplace → Worker cannot travel

2. **If ALLOW rules exist but none match**: Transport is BLOCKED
   - Example: Home has OUTGOING ALLOW rule for Building C, but worker wants to go to Building D → Worker cannot travel

3. **If only DISALLOW rules exist and none match**: Transport is ALLOWED
   - Example: Home has OUTGOING DISALLOW rule for Building C, but worker wants to go to Building D → Worker can travel

4. **If no rules remain after filtering**: Transport is ALLOWED
   - No restrictions apply to this specific journey

## Rule Configuration

### Rule Properties

Each rule contains:
- **Type**: `INCOMING` or `OUTGOING`
- **Allow**: `ALLOW` (whitelist) or `DISALLOW` (blacklist)
- **TransportType**: Must be `Workers` for worker restrictions
- **Buildings**: List of building entity IDs
- **Districts**: List of district entity IDs

### Direction Types

- **OUTGOING**: Controls workers LEAVING from a building/district
  - Configured on the source (home building or district)
  - Targets list specifies where workers CAN or CANNOT go

- **INCOMING**: Controls workers ARRIVING at a building/district
  - Configured on the destination (workplace building or district)
  - Targets list specifies where workers CAN or CANNOT come from

## Examples

### Example 1: Simple DISALLOW (Blacklist)

**Configuration**:
- Building A (residential) has rule:
  - Type: OUTGOING
  - Allow: DISALLOW
  - Buildings: [Building C]

**Behavior**:
- Workers from Building A can work anywhere EXCEPT Building C
- Building C is blacklisted for workers from Building A

### Example 2: Simple ALLOW (Whitelist)

**Configuration**:
- Building A (residential) has rule:
  - Type: OUTGOING
  - Allow: ALLOW
  - Buildings: [Building B, Building C]

**Behavior**:
- Workers from Building A can ONLY work at Buildings B or C
- All other workplaces are implicitly blocked

### Example 3: INCOMING Restriction

**Configuration**:
- Building B (workplace) has rule:
  - Type: INCOMING
  - Allow: ALLOW
  - Districts: [District 1, District 2]

**Behavior**:
- Building B accepts workers ONLY from Districts 1 and 2
- Workers from other districts cannot work at Building B

### Example 4: Building Rules Override District Rules

**Configuration**:
- District 1 (containing Building A) has rule:
  - Type: OUTGOING
  - Allow: DISALLOW
  - Buildings: [Building C]

- Building A has rule:
  - Type: OUTGOING
  - Allow: ALLOW
  - Buildings: [Building C, Building D]

**Behavior**:
- Workers from Building A can work at Buildings C and D
- The building rule overrides the district rule that would have blocked Building C
- District rule is ignored because building rule exists

### Example 5: Multiple Rules Interaction

**Configuration**:
- Building A has rule:
  - Type: OUTGOING
  - Allow: ALLOW
  - Buildings: [Building B, Building C]

- Building B has rule:
  - Type: INCOMING
  - Allow: DISALLOW
  - Buildings: [Building A]

**Behavior**:
- Worker from Building A to Building B:
  - Step 1: Collect both rules
  - Step 2: Filter to OUTGOING from A and INCOMING to B → Both rules apply
  - Step 3: Both are building rules → Keep both
  - Step 4: Evaluate:
    - ALLOW rule: Building B is in the list → matches
    - DISALLOW rule: Building A is in the list → matches, BLOCKS transport
  - Result: **Transport is BLOCKED** (DISALLOW takes priority)

- Worker from Building A to Building C:
  - Step 1: Collect rule from A, no rules from C
  - Step 2: Filter to OUTGOING from A → One rule applies
  - Step 3: Building rule → Keep it
  - Step 4: Evaluate:
    - ALLOW rule: Building C is in the list → matches
  - Result: **Transport is ALLOWED**

## Implementation Notes

### Performance

- The system updates every 128 frames (approximately every half second at 60 FPS)
- Workers violating rules are actively removed from workplaces
- The Worker component is removed from citizens who violate rules

### Default Behavior

- If no rules are configured: All worker transport is allowed
- If rules exist but don't apply to a specific journey: Transport is allowed
- The system is opt-in: only enforces restrictions where explicitly configured

### Entity Management

When a worker violates a rule:
1. The worker is removed from the workplace's employee buffer
2. The Worker component is removed from the citizen entity
3. The citizen will seek new employment that complies with the rules

## Best Practices

1. **Use Districts for Broad Policies**: Apply district-level rules for general zoning policies
2. **Use Buildings for Exceptions**: Use building-level rules to create exceptions to district policies
3. **ALLOW for Strict Control**: Use ALLOW rules when you want to strictly control where workers can go
4. **DISALLOW for Exclusions**: Use DISALLOW rules when you want to block specific destinations while allowing everything else
5. **Test Rules**: Always test rule combinations to ensure they produce the desired behavior
6. **Avoid Conflicting Rules**: Be careful when mixing ALLOW and DISALLOW rules, as DISALLOW always takes priority

## Technical Details

### System Type
- `ResourceChainPathfindSystem` extends `GameSystemBase`
- Update phase: Every 128 frames
- Uses Entity Component System (ECS) architecture

### Key Components
- `Worker`: Component attached to citizens with employment
- `Employee`: Buffer attached to workplace buildings
- `CurrentDistrict`: Component linking buildings to their districts
- `ResourceChainRule`: Configuration data for rules

### Thread Safety
- The system uses `EntityCommandBuffer` for deferred entity modifications
- All entity operations are batched and applied at the end of the frame
