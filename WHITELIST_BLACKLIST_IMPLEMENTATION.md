# Whitelist/Blacklist Implementation Update

## Summary

The worker restriction system has been updated to use a more intuitive whitelist/blacklist approach for rules.

## New Logic

### DISALLOW = Blacklist
**Allow everything EXCEPT listed buildings**

When you set a rule to DISALLOW:
- Workers can travel to ANY building EXCEPT the ones you specifically pick
- This creates a blacklist of forbidden destinations
- Example: "Citizens can work anywhere except Factory A and Factory B"

### ALLOW = Whitelist  
**Allow ONLY listed buildings**

When you set a rule to ALLOW:
- Workers can ONLY travel to the buildings you specifically pick
- All other buildings are automatically blocked
- This creates a whitelist of permitted destinations
- Example: "Citizens can ONLY work at Office A and Office B"

## Rule Types with New Logic

### OUTGOING Rules (from Home)

**DISALLOW (Blacklist)**:
- Residents can work anywhere EXCEPT the listed workplaces
- Block specific workplaces while keeping everything else available

**ALLOW (Whitelist)**:
- Residents can ONLY work at the listed workplaces
- Restrict employment to specific approved workplaces

### INCOMING Rules (to Workplace)

**DISALLOW (Blacklist)**:
- Workers from listed homes CANNOT work here
- Block specific residential areas while accepting everyone else

**ALLOW (Whitelist)**:
- ONLY workers from listed homes can work here
- Create exclusive employment for specific residential areas

## Example Scenarios

### Scenario 1: Blacklist Specific Factories
```
Building: Residential A (ID: 100)
Rule: OUTGOING + DISALLOW + Workers
Buildings List: [Factory B (200), Factory C (201)]
Result: Citizens from Building 100 can work ANYWHERE except Factory 200 and 201
```

### Scenario 2: Whitelist Premium Offices
```
Building: Residential A (ID: 100)
Rule: OUTGOING + ALLOW + Workers
Buildings List: [Office X (300), Office Y (301)]
Result: Citizens from Building 100 can ONLY work at Office 300 and 301
```

### Scenario 3: Block Specific Neighborhoods
```
Building: Factory A (ID: 500)
Rule: INCOMING + DISALLOW + Workers
Buildings List: [Residential X (100), Residential Y (101)]
Result: Factory 500 won't accept workers from Building 100 and 101, but accepts everyone else
```

### Scenario 4: Exclusive Employment
```
Building: Premium Office (ID: 600)
Rule: INCOMING + ALLOW + Workers
Buildings List: [Luxury Residential A (700), Luxury Residential B (701)]
Result: Office 600 ONLY accepts workers from Building 700 and 701
```

## Implementation Details

### Code Changes

**File**: `Code/ManageResourceChains/Systems/ResourceChainPathfindSystem.cs`

**Methods Updated**:
1. `IsWorkerTransportAllowed()` - Main worker validation logic
2. `IsTransportAllowed()` - Generic transport validation (for future use with resources/services)

**Logic Flow**:
```
For each rule:
  1. Check if rule applies (right transport type, right building, right direction)
  2. Check if target is in the rule's building list
  3. Apply whitelist/blacklist logic:
     - DISALLOW + IN LIST = BLOCK (blacklist)
     - DISALLOW + NOT IN LIST = ALLOW
     - ALLOW + IN LIST = ALLOW (whitelist)
     - ALLOW + NOT IN LIST = BLOCK
```

### Logging

The system now provides clear log messages:
- `🚫 Worker BLOCKED (Blacklist): Home X -> Workplace Y`
- `🚫 Worker BLOCKED (Whitelist): Home X -> Workplace Y (not in allowed list)`

### Documentation Updates

**File**: `pathfinding.md`
- Updated "Rule Logic (Whitelist/Blacklist)" section
- Updated all example scenarios
- Clarified DISALLOW = Blacklist, ALLOW = Whitelist

## Testing

To test the new logic:

1. **Test Blacklist (DISALLOW)**:
   - Create a residential building
   - Add OUTGOING + DISALLOW + Workers rule
   - Select 2-3 workplaces to block
   - Verify workers can go to other workplaces but not the blocked ones

2. **Test Whitelist (ALLOW)**:
   - Create a residential building
   - Add OUTGOING + ALLOW + Workers rule
   - Select 2-3 workplaces to allow
   - Verify workers can ONLY go to those workplaces

3. **Test INCOMING Blacklist**:
   - Select a workplace
   - Add INCOMING + DISALLOW + Workers rule
   - Select 2-3 residential buildings to block
   - Verify those residents can't work there but others can

4. **Test INCOMING Whitelist**:
   - Select a workplace
   - Add INCOMING + ALLOW + Workers rule
   - Select 2-3 residential buildings to allow
   - Verify ONLY those residents can work there

## Compatibility

- ✅ Backward compatible - existing rules will continue to work
- ✅ No save file format changes required
- ✅ Works with existing UI
- ✅ Performance remains the same (~2 second update interval)

## Future Extensions

This whitelist/blacklist pattern can be extended to:
- **Resources**: Control which buildings can send/receive specific resources
- **Services**: Control which service vehicles can access buildings
- **Transport**: Control which transport lines citizens can use

The same logic applies: DISALLOW = blacklist, ALLOW = whitelist.

