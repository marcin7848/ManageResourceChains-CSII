using System;
using System.Collections.Generic;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Pathfind;
using Game.Simulation;
using ManageResourceChains.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that intercepts worker pathfinding and applies transport preferences
    /// based on building and district configurations. This system modifies pathfinding
    /// parameters to enforce preferred transport methods for employees commuting to work.
    /// </summary>
    public partial class TransportPreferenceSystem : GameSystemBase
    {
        /// <summary>
        /// Burst-compiled job that checks workers' pathfinding requests and applies transport preferences
        /// </summary>
        [BurstCompile]
        private struct ApplyTransportPreferenceJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityType;
            [ReadOnly] public ComponentTypeHandle<Worker> WorkerType;
            [ReadOnly] public ComponentTypeHandle<HouseholdMember> HouseholdMemberType;
            [ReadOnly] public ComponentTypeHandle<TravelPurpose> TravelPurposeType;

            [ReadOnly] public ComponentLookup<PropertyRenter> PropertyRenterLookup;
            [ReadOnly] public ComponentLookup<CurrentDistrict> CurrentDistrictLookup;
            [ReadOnly] public ComponentLookup<Household> HouseholdLookup;
            [ReadOnly] public ComponentLookup<Citizen> CitizenLookup;

            [ReadOnly] public NativeHashMap<int, TransportPreferenceData> BuildingPreferences;
            [ReadOnly] public NativeHashMap<int, TransportPreferenceData> DistrictPreferences;

            public NativeQueue<TransportPreferenceLogData>.ParallelWriter LogQueue;

            public float TimeOfDay;
            public uint RandomSeed;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityType);
                var workers = chunk.GetNativeArray(ref WorkerType);
                var householdMembers = chunk.GetNativeArray(ref HouseholdMemberType);
                var travelPurposes = chunk.GetNativeArray(ref TravelPurposeType);

                for (int i = 0; i < entities.Length; i++)
                {
                    Entity citizenEntity = entities[i];
                    Worker worker = workers[i];
                    Entity workplace = worker.m_Workplace;

                    // Only apply to workers who are actually going to work
                    if (workplace == Entity.Null)
                        continue;

                    // Check if this citizen is traveling to work
                    if (!chunk.Has(ref TravelPurposeType))
                        continue;

                    TravelPurpose purpose = travelPurposes[i];
                    if (purpose.m_Purpose != Purpose.GoingToWork)
                        continue;

                    // Get the citizen's home building
                    Entity household = householdMembers[i].m_Household;
                    Entity homeBuilding = Entity.Null;

                    if (PropertyRenterLookup.HasComponent(household))
                    {
                        homeBuilding = PropertyRenterLookup[household].m_Property;
                    }

                    if (homeBuilding == Entity.Null)
                        continue;

                    // Get transport preference for this worker
                    int homeId = homeBuilding.Index;
                    int workplaceId = workplace.Index;

                    Entity homeDistrict = GetBuildingDistrictJob(homeBuilding, CurrentDistrictLookup);
                    Entity workplaceDistrict = GetBuildingDistrictJob(workplace, CurrentDistrictLookup);

                    var preference = GetTransportPreferenceForWorker(
                        homeId, workplaceId, homeDistrict, workplaceDistrict,
                        BuildingPreferences, DistrictPreferences, RandomSeed + (uint)citizenEntity.Index);
                    
                    if (preference == PreferredTransport.None)
                        continue;
                    
                    // Only log when there's an actual preference
                    LogQueue.Enqueue(new TransportPreferenceLogData
                    {
                        HomeEntityId = homeId,
                        WorkplaceId = workplaceId,
                        Preference = preference
                    });
                    
                    // Apply preference to pathfinding (would need PathOwner modifications)
                    // Note: PathOwner modifications are complex and may require different approach
                    // This is where we would modify the pathfinding parameters
                    // For now, we mark this as a location where integration would happen
                }
            }

            private static Entity GetBuildingDistrictJob(Entity building,
                ComponentLookup<CurrentDistrict> currentDistrictLookup)
            {
                if (currentDistrictLookup.HasComponent(building))
                {
                    var currentDistrict = currentDistrictLookup[building];
                    if (currentDistrict.m_District != Entity.Null)
                    {
                        return currentDistrict.m_District;
                    }
                }

                return Entity.Null;
            }

            private static PreferredTransport GetTransportPreferenceForWorker(
                int homeBuilding, int workplaceBuilding,
                Entity homeDistrict, Entity workplaceDistrict,
                NativeHashMap<int, TransportPreferenceData> buildingPrefs,
                NativeHashMap<int, TransportPreferenceData> districtPrefs,
                uint randomSeed)
            {
                // Collect enabled transport preferences from workplace building
                var enabledTransports = new NativeList<PreferredTransport>(10, Allocator.Temp);

                // Check workplace building first
                if (buildingPrefs.TryGetValue(workplaceBuilding, out var workplacePref))
                {
                    CollectEnabledTransports(workplacePref, ref enabledTransports);
                }

                // If no building preferences, check workplace district
                if (enabledTransports.Length == 0 && workplaceDistrict != Entity.Null)
                {
                    if (districtPrefs.TryGetValue(workplaceDistrict.Index, out var districtPref))
                    {
                        CollectEnabledTransports(districtPref, ref enabledTransports);
                    }
                }

                // If still no preferences, check home building
                if (enabledTransports.Length == 0 && buildingPrefs.TryGetValue(homeBuilding, out var homePref))
                {
                    CollectEnabledTransports(homePref, ref enabledTransports);
                }

                // If still no preferences, check home district
                if (enabledTransports.Length == 0 && homeDistrict != Entity.Null)
                {
                    if (districtPrefs.TryGetValue(homeDistrict.Index, out var homeDistrictPref))
                    {
                        CollectEnabledTransports(homeDistrictPref, ref enabledTransports);
                    }
                }

                PreferredTransport result = PreferredTransport.None;

                if (enabledTransports.Length > 0)
                {
                    // Randomly pick one if multiple are enabled
                    var random = new Random(randomSeed);
                    int index = random.NextInt(0, enabledTransports.Length);
                    result = enabledTransports[index];
                }

                enabledTransports.Dispose();
                return result;
            }

            private static void CollectEnabledTransports(TransportPreferenceData prefs,
                ref NativeList<PreferredTransport> list)
            {
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Bus) != 0)
                    list.Add(PreferredTransport.Bus);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Train) != 0)
                    list.Add(PreferredTransport.Train);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Tram) != 0)
                    list.Add(PreferredTransport.Tram);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Metro) != 0)
                    list.Add(PreferredTransport.Metro);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Ferry) != 0)
                    list.Add(PreferredTransport.Ferry);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Airplane) != 0)
                    list.Add(PreferredTransport.Airplane);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Taxi) != 0)
                    list.Add(PreferredTransport.Taxi);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Walking) != 0)
                    list.Add(PreferredTransport.Walking);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Bicycle) != 0)
                    list.Add(PreferredTransport.Bicycle);
                if ((prefs.EnabledTransports & TransportPreferenceFlags.Car) != 0)
                    list.Add(PreferredTransport.Car);
            }
        }

        /// <summary>
        /// Blittable transport preference data for use in Burst-compiled jobs
        /// </summary>
        private struct TransportPreferenceData
        {
            public TransportPreferenceFlags EnabledTransports;
        }

        /// <summary>
        /// Logging data collected from Burst jobs for later processing
        /// </summary>
        private struct TransportPreferenceLogData
        {
            public int HomeEntityId;
            public int WorkplaceId;
            public PreferredTransport Preference;
        }

        /// <summary>
        /// Flags representing which transport types are enabled
        /// </summary>
        [Flags]
        private enum TransportPreferenceFlags : ushort
        {
            None = 0,
            Bus = 1 << 0,
            Train = 1 << 1,
            Tram = 1 << 2,
            Metro = 1 << 3,
            Ferry = 1 << 4,
            Airplane = 1 << 5,
            Taxi = 1 << 6,
            Walking = 1 << 7,
            Bicycle = 1 << 8,
            Car = 1 << 9
        }

        /// <summary>
        /// Enum representing preferred transport types
        /// </summary>
        public enum PreferredTransport : byte
        {
            None = 0,
            Bus = 1,
            Train = 2,
            Tram = 3,
            Metro = 4,
            Ferry = 5,
            Airplane = 6,
            Taxi = 7,
            Walking = 8,
            Bicycle = 9,
            Car = 10
        }

        private ResourceChainManagementSystem m_ResourceChainManagementSystem;
        private EntityQuery m_WorkerPathfindingQuery;
        private SimulationSystem m_SimulationSystem;
        
        // Track which workers we've already logged to avoid spam
        private readonly HashSet<int> m_LoggedWorkers = new HashSet<int>();
        private uint m_LastCleanupFrame = 0;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Update every 16 frames (about 4 times per second at 60fps)
            // More frequent than ResourceChainRulesSystem as pathfinding needs quicker response
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResourceChainManagementSystem = World.GetOrCreateSystemManaged<ResourceChainManagementSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            // Query for citizens who are workers with TravelPurpose (actively traveling)
            // Note: PathOwner may not always be present when pathfinding is requested
            m_WorkerPathfindingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Worker>(),
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                    ComponentType.ReadOnly<TravelPurpose>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>()
                }
            });

            Mod.log.Info($"{nameof(TransportPreferenceSystem)} created - Transport preference enforcement enabled");
        }

        protected override void OnUpdate()
        {
            // Convert managed configs to native data structures for the job
            var buildingPreferences = ConvertToNativePreferences(
                m_ResourceChainManagementSystem.GetAllConfigurations());
            var districtPreferences = ConvertToNativePreferences(
                m_ResourceChainManagementSystem.GetAllDistrictConfigurations());
            
            // Clean up logged workers tracking every 262144 frames (about every in-game day)
            uint currentFrame = m_SimulationSystem.frameIndex;
            if (currentFrame - m_LastCleanupFrame > 262144)
            {
                m_LoggedWorkers.Clear();
                m_LastCleanupFrame = currentFrame;
            }
            
            // Only proceed if there are preferences to apply
            if (buildingPreferences.Count > 0 || districtPreferences.Count > 0)
            {
                // Create a queue to collect logging data from the job
                var logQueue = new NativeQueue<TransportPreferenceLogData>(Allocator.TempJob);

                // Get current time of day for public transport availability
                float timeOfDay = (float)(m_SimulationSystem.frameIndex % 262144) / 262144f;

                var job = new ApplyTransportPreferenceJob
                {
                    EntityType = GetEntityTypeHandle(),
                    WorkerType = GetComponentTypeHandle<Worker>(true),
                    HouseholdMemberType = GetComponentTypeHandle<HouseholdMember>(true),
                    TravelPurposeType = GetComponentTypeHandle<TravelPurpose>(true),
                    PropertyRenterLookup = GetComponentLookup<PropertyRenter>(true),
                    CurrentDistrictLookup = GetComponentLookup<CurrentDistrict>(true),
                    HouseholdLookup = GetComponentLookup<Household>(true),
                    CitizenLookup = GetComponentLookup<Citizen>(true),
                    BuildingPreferences = buildingPreferences,
                    DistrictPreferences = districtPreferences,
                    LogQueue = logQueue.AsParallelWriter(),
                    TimeOfDay = timeOfDay,
                    RandomSeed = (uint)m_SimulationSystem.frameIndex
                };

                // Schedule the job to run in parallel
                Dependency = job.ScheduleParallel(m_WorkerPathfindingQuery, Dependency);
                Dependency.Complete();

                // Process logging data - only log if we haven't logged this worker before
                int logCount = 0;
                while (logQueue.TryDequeue(out var logData))
                {
                    // Create a unique key for this worker trip (home + workplace)
                    // Simple hash: combine the two IDs
                    int tripKey = (logData.HomeEntityId * 397) ^ logData.WorkplaceId;
                    
                    if (m_LoggedWorkers.Add(tripKey))
                    {
                        // Only log if this is the first time we see this worker trip
                        LogTransportPreference(logData.HomeEntityId, logData.WorkplaceId, logData.Preference);
                        logCount++;
                    }
                }

                // Cleanup log queue
                logQueue.Dispose();
            }

            // Cleanup
            buildingPreferences.Dispose();
            districtPreferences.Dispose();
        }

        /// <summary>
        /// Convert managed building configurations to native transport preference data
        /// </summary>
        private NativeHashMap<int, TransportPreferenceData> ConvertToNativePreferences(
            Dictionary<int, BuildingConfiguration> configs)
        {
            var nativePrefs = new NativeHashMap<int, TransportPreferenceData>(configs.Count, Allocator.TempJob);

            foreach (var kvp in configs)
            {
                int entityId = kvp.Key;
                var config = kvp.Value;

                if (config.Rules == null || config.Rules.Count == 0)
                    continue;

                // Collect all transport preferences for worker rules
                TransportPreferenceFlags flags = TransportPreferenceFlags.None;

                foreach (var rule in config.Rules)
                {
                    // Only process worker transport rules
                    if (rule.TransportType != TransportType.Workers)
                        continue;

                    var prefs = rule.TransportPreferences;
                    if (prefs == null)
                        continue;

                    // Collect enabled transport types
                    if (prefs.Bus) flags |= TransportPreferenceFlags.Bus;
                    if (prefs.Train) flags |= TransportPreferenceFlags.Train;
                    if (prefs.Tram) flags |= TransportPreferenceFlags.Tram;
                    if (prefs.Metro) flags |= TransportPreferenceFlags.Metro;
                    if (prefs.Ferry) flags |= TransportPreferenceFlags.Ferry;
                    if (prefs.Airplane) flags |= TransportPreferenceFlags.Airplane;
                    if (prefs.Taxi) flags |= TransportPreferenceFlags.Taxi;
                    if (prefs.Walking) flags |= TransportPreferenceFlags.Walking;
                    if (prefs.Bicycle) flags |= TransportPreferenceFlags.Bicycle;
                    if (prefs.Car) flags |= TransportPreferenceFlags.Car;
                }

                if (flags != TransportPreferenceFlags.None)
                {
                    nativePrefs.Add(entityId, new TransportPreferenceData
                    {
                        EnabledTransports = flags
                    });
                }
            }

            return nativePrefs;
        }

        /// <summary>
        /// Get pathfinding weights modified for transport preference.
        /// This method calculates appropriate weights that bias the pathfinding
        /// towards the preferred transport method.
        /// </summary>
        public static PathfindWeights GetModifiedWeightsForPreference(
            Citizen citizen,
            Household household,
            int householdCitizens,
            PreferredTransport preference)
        {
            // Get base weights using similar formula to CitizenUtils.GetPathfindWeights
            float time = 5f * (4f - 3.75f * (float)(int)citizen.m_LeisureCounter / 255f);
            float behaviour = 2f;
            float money = 2500f * math.max(1f, householdCitizens) / (float)math.max(250, household.m_ConsumptionPerDay);
            float comfort = 1f + 2f * citizen.GetPseudoRandom(CitizenPseudoRandom.TrafficComfort).NextFloat();

            // Check if household has moved in
            bool movedIn = (household.m_Flags & HouseholdFlags.MovedIn) != 0;
            bool isSpecialCitizen = (citizen.m_State &
                                     (CitizenFlags.MovingAwayReachOC | CitizenFlags.Tourist | CitizenFlags.Commuter)) !=
                                    0;
            money = math.select(money, money * 0.1f, !movedIn && !isSpecialCitizen);

            // Apply preference modifications
            switch (preference)
            {
                case PreferredTransport.Bus:
                case PreferredTransport.Train:
                case PreferredTransport.Metro:
                case PreferredTransport.Tram:
                case PreferredTransport.Ferry:
                case PreferredTransport.Airplane:
                    // Heavy preference for public transport
                    // Reduce time concern (public transport may take longer routes)
                    time *= 0.1f;
                    // Drastically reduce money weight (make ticket cost negligible)
                    money *= 0.01f;
                    // Reduce comfort weight (accept crowding)
                    comfort *= 0.1f;
                    break;

                case PreferredTransport.Taxi:
                    // Taxi preference - don't care about cost
                    money *= 0.01f;
                    comfort *= 0.5f;
                    break;

                case PreferredTransport.Walking:
                    // Walking preference - time matters less, no cost
                    time *= 0.3f;
                    money = 0.01f;
                    break;

                case PreferredTransport.Bicycle:
                    // Bicycle preference
                    time *= 0.5f;
                    money *= 0.1f;
                    comfort *= 0.5f;
                    break;

                case PreferredTransport.Car:
                    // Car preference - don't care about parking/fuel cost
                    time *= 0.5f;
                    money *= 0.1f;
                    comfort *= 0.5f;
                    break;

                case PreferredTransport.None:
                default:
                    // No modification
                    break;
            }

            return new PathfindWeights(time, behaviour, money, comfort);
        }

        /// <summary>
        /// Get restricted path methods based on transport preference.
        /// This can be used to force only certain transport methods.
        /// </summary>
        public static PathMethod GetRestrictedPathMethodsForPreference(PreferredTransport preference, float timeOfDay)
        {
            PathMethod methods = PathMethod.Pedestrian;

            const float DAY_START = 0.25f; // 6 AM
            const float DAY_END = 11f / 12f; // 10 PM
            bool isDay = timeOfDay >= DAY_START && timeOfDay < DAY_END;

            PathMethod publicTransport = isDay ? PathMethod.PublicTransportDay : PathMethod.PublicTransportNight;

            switch (preference)
            {
                case PreferredTransport.Bus:
                case PreferredTransport.Train:
                case PreferredTransport.Metro:
                case PreferredTransport.Tram:
                case PreferredTransport.Ferry:
                case PreferredTransport.Airplane:
                    // Only allow public transport + walking
                    methods |= publicTransport;
                    break;

                case PreferredTransport.Taxi:
                    methods |= PathMethod.Taxi;
                    break;

                case PreferredTransport.Walking:
                    // Only walking (pedestrian already set)
                    break;

                case PreferredTransport.Bicycle:
                    methods |= PathMethod.Bicycle | PathMethod.BicycleParking;
                    break;

                case PreferredTransport.Car:
                    methods |= PathMethod.Road | PathMethod.Parking;
                    break;

                case PreferredTransport.None:
                default:
                    // Allow all methods
                    methods |= PathMethod.Road | PathMethod.Parking | PathMethod.Taxi |
                               PathMethod.Bicycle | PathMethod.BicycleParking | publicTransport;
                    break;
            }

            return methods;
        }

        /// <summary>
        /// Logs transport preference application for debugging purposes.
        /// Cannot be called from Burst jobs, so must be used separately.
        /// </summary>
        public static void LogTransportPreference(int homeEntityId, int workplaceId, PreferredTransport preference)
        {
            Mod.log.Info($"[TransportPreference] Home: {homeEntityId}, Workplace: {workplaceId}, Preference: {preference}");
        }

        /// <summary>
        /// Public API to get transport preference for a specific worker.
        /// This can be called from other systems or Harmony patches.
        /// </summary>
        public static PreferredTransport GetPreferenceForWorker(
            Entity citizenEntity,
            Entity homeBuilding,
            Entity workplace,
            ComponentLookup<CurrentDistrict> districtLookup)
        {
            try
            {
                var mgmtSystem = World.DefaultGameObjectInjectionWorld?
                    .GetOrCreateSystemManaged<ResourceChainManagementSystem>();

                if (mgmtSystem == null)
                    return PreferredTransport.None;

                var buildingConfigs = mgmtSystem.GetAllConfigurations();
                var districtConfigs = mgmtSystem.GetAllDistrictConfigurations();

                Entity homeDistrict = GetDistrict(homeBuilding, districtLookup);
                Entity workplaceDistrict = GetDistrict(workplace, districtLookup);

                return GetPreferenceInternal(
                    homeBuilding.Index,
                    workplace.Index,
                    homeDistrict,
                    workplaceDistrict,
                    buildingConfigs,
                    districtConfigs,
                    (uint)citizenEntity.Index);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Error getting preference for worker: {ex.Message}");
                return PreferredTransport.None;
            }
        }

        private static Entity GetDistrict(Entity building, ComponentLookup<CurrentDistrict> lookup)
        {
            if (lookup.HasComponent(building))
            {
                var district = lookup[building];
                if (district.m_District != Entity.Null)
                    return district.m_District;
            }

            return Entity.Null;
        }

        private static PreferredTransport GetPreferenceInternal(
            int homeBuilding,
            int workplaceBuilding,
            Entity homeDistrict,
            Entity workplaceDistrict,
            Dictionary<int, BuildingConfiguration> buildingConfigs,
            Dictionary<int, BuildingConfiguration> districtConfigs,
            uint randomSeed)
        {
            var enabledTransports = new List<PreferredTransport>();

            // Check workplace building first
            if (buildingConfigs.TryGetValue(workplaceBuilding, out var workplaceConfig))
            {
                CollectEnabledTransportsFromConfig(workplaceConfig, enabledTransports);
            }

            // If no building preferences, check workplace district
            if (enabledTransports.Count == 0 && workplaceDistrict != Entity.Null)
            {
                if (districtConfigs.TryGetValue(workplaceDistrict.Index, out var districtConfig))
                {
                    CollectEnabledTransportsFromConfig(districtConfig, enabledTransports);
                }
            }

            // If still no preferences, check home building
            if (enabledTransports.Count == 0 && buildingConfigs.TryGetValue(homeBuilding, out var homeConfig))
            {
                CollectEnabledTransportsFromConfig(homeConfig, enabledTransports);
            }

            // If still no preferences, check home district
            if (enabledTransports.Count == 0 && homeDistrict != Entity.Null)
            {
                if (districtConfigs.TryGetValue(homeDistrict.Index, out var homeDistrictConfig))
                {
                    CollectEnabledTransportsFromConfig(homeDistrictConfig, enabledTransports);
                }
            }

            if (enabledTransports.Count == 0)
                return PreferredTransport.None;

            // Randomly pick one if multiple are enabled
            var random = new Random(randomSeed);
            int index = random.NextInt(0, enabledTransports.Count);
            return enabledTransports[index];
        }

        private static void CollectEnabledTransportsFromConfig(BuildingConfiguration config,
            List<PreferredTransport> list)
        {
            if (config.Rules == null)
                return;

            foreach (var rule in config.Rules)
            {
                if (rule.TransportType != TransportType.Workers)
                    continue;

                var prefs = rule.TransportPreferences;
                if (prefs == null)
                    continue;

                if (prefs.Bus && !list.Contains(PreferredTransport.Bus))
                    list.Add(PreferredTransport.Bus);
                if (prefs.Train && !list.Contains(PreferredTransport.Train))
                    list.Add(PreferredTransport.Train);
                if (prefs.Tram && !list.Contains(PreferredTransport.Tram))
                    list.Add(PreferredTransport.Tram);
                if (prefs.Metro && !list.Contains(PreferredTransport.Metro))
                    list.Add(PreferredTransport.Metro);
                if (prefs.Ferry && !list.Contains(PreferredTransport.Ferry))
                    list.Add(PreferredTransport.Ferry);
                if (prefs.Airplane && !list.Contains(PreferredTransport.Airplane))
                    list.Add(PreferredTransport.Airplane);
                if (prefs.Taxi && !list.Contains(PreferredTransport.Taxi))
                    list.Add(PreferredTransport.Taxi);
                if (prefs.Walking && !list.Contains(PreferredTransport.Walking))
                    list.Add(PreferredTransport.Walking);
                if (prefs.Bicycle && !list.Contains(PreferredTransport.Bicycle))
                    list.Add(PreferredTransport.Bicycle);
                if (prefs.Car && !list.Contains(PreferredTransport.Car))
                    list.Add(PreferredTransport.Car);
            }
        }
    }
}