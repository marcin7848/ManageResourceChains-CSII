# Quick Reference - Service & Resource Rules

## For Users

### Service Types You Can Control

**Emergency Services** (go TO buildings):
- 🏥 Healthcare - Hospitals, ambulances, medical helicopters
- ⚰️ Deathcare - Cemeteries, crematoriums, hearses  
- 🚒 Fire - Fire stations, fire engines, fire helicopters
- 🚓 Police - Police stations, police cars, police helicopters
- 🚛 Garbage - Garbage facilities, garbage trucks
- 📬 Post - Post offices, post vans
- 🏚️ Evacuation - Emergency shelters, evacuation buses
- 🔧 Maintenance - Maintenance depots, maintenance vehicles

**Resource Transport** (materials between buildings):
- 🌾 Raw Materials - Grain, livestock, wood, stone, coal, oil, ore
- 🏭 Processed Goods - Food, textiles, metals, electronics
- 🏬 Commercial Goods - Retail goods
- ⚡ Electricity - Power distribution
- 💧 Water - Water and sewage

### Rule Types

**ALLOW** = Whitelist (only specified buildings/districts allowed)
**DISALLOW** = Blacklist (specified buildings/districts blocked)

**OUTGOING** = From this building (service provider)
**INCOMING** = To this building (service recipient)

### Examples

#### Hospital serves only rich district
```
Hospital A → OUTGOING → ALLOW → Services (Healthcare) → Rich District
```

#### Factory accepts materials only from specific suppliers
```
Factory B → INCOMING → ALLOW → Resources (Raw Materials) → Farm C, Farm D
```

#### Fire station avoids industrial zone
```
Fire Station E → OUTGOING → DISALLOW → Services (Fire) → Industrial District
```

## For Developers

### API Quick Start

**Check if service is allowed:**
```csharp
var rulesSystem = World.GetOrCreateSystemManaged<ResourceChainRulesSystem>();
bool allowed = rulesSystem.IsServiceTransportAllowed(
    hospitalEntityId, 
    targetBuildingId
);
```

**Check if resource transport is allowed:**
```csharp
bool allowed = rulesSystem.IsResourceTransportAllowed(
    sourceBuildingId,
    destinationBuildingId
);
```

### Service Types Enum
```csharp
public enum ServiceType
{
    All, Healthcare, Deathcare, Fire, Police,
    Garbage, Post, Evacuation, Maintenance
}
```

### Resource Types Enum
```csharp
public enum ResourceType
{
    All, RawMaterials, ProcessedGoods,
    CommercialGoods, Electricity, Water
}
```

### Rule Configuration Structure
```csharp
{
    "BuildingEntityId": 12345,
    "Rules": [{
        "Type": "Outgoing",              // or "Incoming"
        "Allow": "Allow",                 // or "Disallow"
        "TransportType": "Services",      // or "Workers" or "Resources"
        "Buildings": [67890, 67891],      // target buildings
        "Districts": [11111]              // target districts
    }]
}
```

**Note**: Each building only provides ONE type of service or resource, so `TransportType` alone determines what is being restricted. No need for service/resource subtypes.

### Implementation Status

| Component | Status | Notes |
|-----------|--------|-------|
| Data Model | ✅ Complete | ServiceType, ResourceType enums |
| Rule Checking | ✅ Complete | IsServiceTransportAllowed(), IsResourceTransportAllowed() |
| System Framework | ✅ Complete | ServiceRulesInterceptSystem |
| Pathfinding Hooks | ⏳ Template | Needs completion per service |
| UI Integration | ❌ Not Started | Add service/resource type selectors |
| Testing | ❌ Not Started | Create test scenarios |

### Key Files

**Core Logic:**
- `Data/ResourceChainConfig.cs` - Enums and data structures
- `Systems/ResourceChainRulesSystem.cs` - Rule evaluation logic

**Service Integration:**
- `Systems/ServiceRulesInterceptSystem.cs` - Service validation
- `Systems/ServicePathfindingPatches.cs` - Harmony patches (template)

**Documentation:**
- `ServiceAndResourceRules.md` - Complete technical docs
- `HealthcareImplementationGuide.md` - Implementation guide
- `IMPLEMENTATION_SUMMARY.md` - Full summary

### Next Steps for Implementation

1. **Complete Healthcare Patch** (8-12 hours)
   - Edit `ServicePathfindingPatches.cs`
   - Patch `HealthcarePathfindSetup.SetupAmbulancesJob`
   - Test with hospitals

2. **Extend to Fire Service** (6-10 hours)
   - Patch `FirePathfindSetup.SetupFireEnginesJob`
   - Test with fire stations

3. **Add Other Services** (15-20 hours)
   - Police, Garbage, Post
   - Test each service type

4. **Add UI Support** (10-15 hours)
   - Service type dropdown
   - Resource type dropdown
   - Visual indicators

### Pathfinding Interception Points

**Healthcare:**
- `Game.Simulation.HealthcarePathfindSetup.SetupAmbulancesJob.Execute()`
- Check before: `targetSeeker.FindTargets(hospitalEntity, cost)`

**Fire:**
- `Game.Simulation.FirePathfindSetup.SetupFireEnginesJob.Execute()`
- Check before: `targetSeeker.FindTargets(fireStationEntity, cost)`

**Police:**
- `Game.Simulation.PolicePathfindSetup.SetupPolicePatrolsJob.Execute()`
- Check before: `targetSeeker.FindTargets(policeStationEntity, cost)`

**Garbage:**
- `Game.Simulation.GarbagePathfindSetup.SetupGarbageCollectorsJob.Execute()`
- Check before: `targetSeeker.FindTargets(garbageFacilityEntity, cost)`

### Testing Checklist

- [ ] Healthcare rule blocks ambulance dispatch
- [ ] Fire rule zones fire station coverage
- [ ] Police rule controls patrol areas
- [ ] Garbage rule restricts collection zones
- [ ] Resource rule controls supply chains
- [ ] Building rules override district rules
- [ ] ALLOW rules work as whitelist
- [ ] DISALLOW rules work as blacklist
- [ ] Performance acceptable with 100+ rules
- [ ] No errors in game logs

### Performance Tips

1. **Check if rules exist first** - Fast path for no rules
2. **Use caching** - Cache rule lookups in NativeHashMap
3. **Burst compile** - Enable Burst for rule checking jobs
4. **Batch operations** - Process multiple requests together
5. **Profile regularly** - Use Unity profiler to identify bottlenecks

### Common Issues

**Rules not working:**
- Check if rules are saved to ResourceChainManagementSystem
- Verify entity IDs are correct
- Check pathfinding patches are applied
- Look for errors in game logs

**Performance issues:**
- Too many rules? Simplify with district rules
- Caching disabled? Enable rule caching
- Not using Burst? Enable Burst compilation

**Game crashes:**
- Harmony patch error? Check patch was applied correctly
- Null reference? Add null checks in service methods
- Entity version mismatch? Use only entity Index for comparisons

### Resources

- **Harmony Docs**: https://harmony.pardeike.net/
- **Unity ECS**: https://docs.unity3d.com/Packages/com.unity.entities@latest
- **Cities Skylines 2 Modding**: Community Discord
- **Decompiled Code**: `Code/ManageResourceChains/other/Decompiled/`

---

## Cheat Sheet

### Rule Logic Quick Reference

```
No rules configured = ALLOW (vanilla behavior)

DISALLOW + match = BLOCK (always)
ALLOW + no match = BLOCK (whitelist)
ALLOW + match = ALLOW
Neither = ALLOW

Building rules > District rules (priority)
```

### Entity ID Quick Get

```csharp
// From entity
int buildingId = entity.Index;

// From name/inspector
// Use BuildingPickerToolSystem or DistrictPickerToolSystem
```

### Debug Logging

```csharp
// In rule checking method
Mod.log.Info($"Checking {serviceType} from {sourceId} to {targetId}: {result}");
```

---

**Last Updated**: February 1, 2026
**Version**: 1.0 (Foundation Complete)
**Status**: Ready for Pathfinding Implementation
