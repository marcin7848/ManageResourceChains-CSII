# Districts in Cities: Skylines II - Technical Documentation

## Overview
Districts in Cities: Skylines II are special area entities that group multiple buildings together. They allow players to apply policies and settings to entire neighborhoods rather than individual buildings.

## District Components

### 1. **District Component** (`Game.Areas.District`)
```csharp
public struct District : IComponentData
{
    public uint m_OptionMask; // Stores district options/settings as a bitmask
}
```

### 2. **CurrentDistrict Component** (`Game.Areas.CurrentDistrict`)
```csharp
public struct CurrentDistrict : IComponentData
{
    public Entity m_District; // Reference to the district entity
}
```
- This component is attached to **buildings** to indicate which district they belong to
- Each building can belong to one district

### 3. **ServiceDistrict Component** (`Game.Areas.ServiceDistrict`)
```csharp
public struct ServiceDistrict : IBufferElementData
{
    public Entity m_District; // Reference to a district
}
```
- This is a **buffer component** that can contain multiple district references
- Used by service buildings (fire stations, police stations, etc.) to define their service areas
- A single building can service multiple districts

## How Districts Work

### District Creation
1. Player uses the **Area Tool** to draw district boundaries
2. System creates an **Entity** with `District` component
3. District gets assigned a name and options

### Building-District Relationship
1. When a building is placed/moved, the system checks which district area it's in
2. System adds/updates the `CurrentDistrict` component on the building with the district entity reference
3. The building now "belongs" to that district

### Service District Assignment
1. Service buildings (police, fire, healthcare) have a `ServiceDistrict` buffer
2. Players can manually assign which districts a service building serves
3. The buffer can contain multiple district entities
4. This controls which buildings the service will respond to

## UI Integration

### DistrictsSection Component
The game's UI shows district information through `DistrictsSection`:

```csharp
// Located in: Game.UI.InGame.DistrictsSection
- Shows list of districts a building belongs to/services
- Provides "Add District" and "Remove District" buttons
- Has district selection tool integration
```

### Key UI Features:
1. **District List**: Shows all districts associated with selected entity
2. **Selection Tool**: Allows clicking districts on the map to add/remove them
3. **District Tool**: Opens the area tool to create new districts

## Queries and Systems

### Important Entity Queries:
```csharp
// Get all districts
EntityQuery districtQuery = GetEntityQuery(
    ComponentType.ReadOnly<District>(), 
    ComponentType.Exclude<Temp>()
);

// Get buildings in a district (conceptually)
// Search for entities with CurrentDistrict.m_District == targetDistrict
```

### Related Systems:
- **AreaToolSystem**: Creates and modifies district boundaries
- **CurrentDistrictSystem**: Updates building-district relationships
- **ServiceDistrictSystem**: Manages service area assignments

## Data Flow

```
1. District Creation
   Area Tool → District Entity (with District component) → District Prefab Data

2. Building Assignment
   Building Position → CurrentDistrictSystem → CurrentDistrict component on building

3. Service Assignment  
   Player Selection → DistrictsSection UI → ServiceDistrict buffer on service building

4. District Queries
   Building → CurrentDistrict.m_District → District Entity
   Service Building → ServiceDistrict buffer → Multiple District Entities
```

## Integration with Resource Chain Management

### For Our Mod:
1. **District Configuration**: Store rules per district entity
2. **Building Resolution**: When checking a building's rules:
   - Check if building has `CurrentDistrict` component
   - If yes, load district's resource chain rules
   - Apply district rules to all buildings in that district
3. **UI Extension**: Add "Manage Resource Chains" button to district panel
4. **Configuration Storage**: Similar to building config, but keyed by district entity

### Implementation Strategy:
```csharp
// Check if building is in a district
if (EntityManager.TryGetComponent<CurrentDistrict>(building, out var currentDistrict))
{
    Entity districtEntity = currentDistrict.m_District;
    // Load district rules instead of/in addition to building rules
    var districtRules = GetDistrictRules(districtEntity);
    // Apply rules...
}
```

## Important Notes

1. **Entity Lifecycle**: Districts are persistent entities that survive across game sessions
2. **Entity Index**: District entities use the standard Entity.Index for identification
3. **Prefabs**: Districts have prefab data (`DistrictData`) that defines their properties
4. **Boundaries**: District boundaries are defined by `Area` component with geometry data
5. **Multiple Buildings**: A single district can contain hundreds of buildings
6. **Service Areas**: Service buildings can serve multiple districts simultaneously

## Files to Reference

### Game Files (Decompiled):
- `Game.Areas.District.cs` - District component definition
- `Game.Areas.CurrentDistrict.cs` - Building-district link
- `Game.Areas.ServiceDistrict.cs` - Service area buffer
- `Game.Areas.CurrentDistrictSystem.cs` - Updates district assignments
- `Game.UI.InGame.DistrictsSection.cs` - District UI panel

### Our Mod Files to Create/Modify:
- `DistrictSelectionUISystem.cs` - New system for district panel button
- `ResourceChainManagementSystem.cs` - Extend to support districts
- `building-button.tsx` - Modify to support district entities
- `DistrictConfiguration.cs` - Data model for district rules

## Summary

Districts are area-based grouping mechanisms that:
- Allow batch management of buildings
- Use entity-component system for relationships
- Support service area definitions
- Integrate with the game's area tool system
- Persist across game sessions

For our resource chain management mod, we can leverage districts to:
- Apply rules to entire neighborhoods at once
- Override individual building rules with district-wide policies
- Simplify large-scale resource flow management
- Provide intuitive UI at the district level

