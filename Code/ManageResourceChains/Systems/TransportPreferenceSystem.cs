using Unity.Entities;
using Unity.Mathematics;
using Game;
using Game.Citizens;
using Game.Common;
using Game.Pathfind;
using Game.Simulation;
using Game.Tools;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that modifies pathfinding weights for workers to prefer specific transport types.
    /// This works by adjusting the PathfindWeights (time, behaviour, money, comfort) to make
    /// certain transport methods significantly cheaper in the pathfinding cost calculation.
    /// 
    /// The pathfinding cost formula is: cost = dot(PathfindCosts.m_Value, PathfindWeights.m_Value)
    /// Where PathfindCosts = (time, behaviour, money, comfort)
    /// 
    /// By reducing the money weight and comfort weight, public transport becomes more attractive
    /// since it has low money cost and comfort is less penalized.
    /// </summary>
    public partial class TransportPreferenceSystem : GameSystemBase
    {
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_PathfindingCitizenQuery;
        
        // Component lookups
        private ComponentLookup<Citizen> m_CitizenLookup;
        private ComponentLookup<Worker> m_WorkerLookup;
        private ComponentLookup<TravelPurpose> m_TravelPurposeLookup;
        private ComponentLookup<PathInformation> m_PathInformationLookup;
        private ComponentLookup<HouseholdMember> m_HouseholdMemberLookup;
        private ComponentLookup<Household> m_HouseholdLookup;
        private BufferLookup<HouseholdCitizen> m_HouseholdCitizenLookup;
        
        /// <summary>
        /// The preferred transport method for workers.
        /// This can be extended to be configurable per building/district.
        /// </summary>
        public enum PreferredTransportMethod
        {
            None,           // No preference, use default pathfinding
            Bus,            // Prefer bus specifically
            Train,          // Prefer train specifically
            Metro,          // Prefer metro/subway specifically
            Tram,           // Prefer tram specifically
            Ferry,          // Prefer ferry specifically
            Airplane,       // Prefer airplane specifically
            Taxi,           // Prefer taxi
            Walking,        // Prefer walking
            Bicycle,        // Prefer bicycle
            Car             // Prefer personal car
        }
        
        // Default preference - train transport
        public static PreferredTransportMethod DefaultPreference = PreferredTransportMethod.Train;
        
        // How much to boost the preferred transport (higher = stronger preference)
        // This multiplier reduces the effective cost of the preferred transport
        public static float PreferenceBoostMultiplier = 0.1f; // 90% cost reduction
        
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            
            // Query for citizens with pending pathfinding
            m_PathfindingCitizenQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<PathInformation>()
                },
                None = new[] { 
                    ComponentType.ReadOnly<Deleted>(), 
                    ComponentType.ReadOnly<Temp>() 
                }
            });
            
            m_CitizenLookup = GetComponentLookup<Citizen>(true);
            m_WorkerLookup = GetComponentLookup<Worker>(true);
            m_TravelPurposeLookup = GetComponentLookup<TravelPurpose>(true);
            m_PathInformationLookup = GetComponentLookup<PathInformation>(true);
            m_HouseholdMemberLookup = GetComponentLookup<HouseholdMember>(true);
            m_HouseholdLookup = GetComponentLookup<Household>(true);
            m_HouseholdCitizenLookup = GetBufferLookup<HouseholdCitizen>(true);
        }
        
        protected override void OnUpdate()
        {
            // This system doesn't need to do per-frame processing
            // The actual pathfinding modification happens via the PathfindSetupSystem hooks
            // which we cannot directly modify from a mod.
            //
            // Instead, our approach is to modify transport stop/line attractiveness
            // through the TransportPriorityCostSystem, which adjusts comfort factors
            // and ticket prices.
            //
            // For more aggressive transport preference, we would need to:
            // 1. Hook into PathfindSetupSystem (requires Harmony patching)
            // 2. Modify PathfindParameters.m_Weights before pathfinding starts
            //
            // The TransportPriorityCostSystem handles the softer approach through
            // game mechanics (comfort, price adjustments).
        }
        
        /// <summary>
        /// Calculate modified pathfind weights that favor public transport.
        /// Call this from a Harmony patch of TripNeededSystem.CitizenJob to override weights.
        /// </summary>
        public static PathfindWeights GetModifiedWeightsForPublicTransport(
            Citizen citizen, 
            Household household, 
            int householdCitizens,
            PreferredTransportMethod preference)
        {
            // Get base weights using the same formula as CitizenUtils.GetPathfindWeights
            float time = 5f * (4f - 3.75f * (float)(int)citizen.m_LeisureCounter / 255f);
            float behaviour = 2f;
            float money = 2500f * math.max(1f, householdCitizens) / (float)math.max(250, household.m_ConsumptionPerDay);
            float comfort = 1f + 2f * citizen.GetPseudoRandom(CitizenPseudoRandom.TrafficComfort).NextFloat();
            
            // Check if household has moved in
            bool movedIn = (household.m_Flags & HouseholdFlags.MovedIn) != 0;
            bool isSpecialCitizen = (citizen.m_State & (CitizenFlags.MovingAwayReachOC | CitizenFlags.Tourist | CitizenFlags.Commuter)) != 0;
            money = math.select(money, money * 0.1f, !movedIn && !isSpecialCitizen);
            
            // Now apply preference modifications
            if (preference == PreferredTransportMethod.None)
            {
                return new PathfindWeights(time, behaviour, money, comfort);
            }
            
            // For public transport preference:
            // - Reduce time weight (public transport takes more time but we don't care as much)
            // - Reduce money weight significantly (makes ticket cost negligible)
            // - Reduce comfort weight (we accept less comfort for public transport)
            
            switch (preference)
            {
                case PreferredTransportMethod.Bus:
                case PreferredTransportMethod.Train:
                case PreferredTransportMethod.Metro:
                case PreferredTransportMethod.Tram:
                case PreferredTransportMethod.Ferry:
                case PreferredTransportMethod.Airplane:
                    // Heavy preference for public transport
                    // By reducing time and comfort weights, public transport becomes very attractive
                    time *= PreferenceBoostMultiplier;      // 90% reduction in time weight
                    money *= PreferenceBoostMultiplier;     // 90% reduction in money weight
                    comfort *= PreferenceBoostMultiplier;   // 90% reduction in comfort weight
                    // Keep behaviour weight to maintain some sanity in pathfinding
                    break;
                    
                case PreferredTransportMethod.Taxi:
                    // Taxi preference - reduce money concern
                    money *= PreferenceBoostMultiplier;
                    comfort *= 0.5f;
                    break;
                    
                case PreferredTransportMethod.Walking:
                    // Walking preference - time matters less, money not at all
                    time *= 0.3f;
                    money = 0.01f;
                    break;
                    
                case PreferredTransportMethod.Bicycle:
                    // Bicycle preference
                    time *= 0.5f;
                    money *= 0.1f;
                    comfort *= 0.5f;
                    break;
                    
                case PreferredTransportMethod.Car:
                    // Car preference - don't care about parking/fuel cost
                    time *= 0.5f;       // Time still matters somewhat
                    money *= 0.1f;      // Don't care about cost
                    comfort *= 0.5f;
                    break;
            }
            
            return new PathfindWeights(time, behaviour, money, comfort);
        }
        
        /// <summary>
        /// Get modified PathMethod flags to restrict transport options.
        /// This can be used to force only certain transport methods.
        /// </summary>
        public static PathMethod GetRestrictedPathMethods(PreferredTransportMethod preference, float timeOfDay)
        {
            // Base methods always include pedestrian
            PathMethod methods = PathMethod.Pedestrian;
            
            // Get public transport methods based on time of day
            // Day: 0.25 to 11/12 (0.916...), Night: rest
            PathMethod publicTransportMethods = GetPublicTransportMethods(timeOfDay);
            
            switch (preference)
            {
                case PreferredTransportMethod.None:
                    // Allow all methods
                    methods |= PathMethod.Taxi | publicTransportMethods;
                    break;
                    
                case PreferredTransportMethod.Bus:
                case PreferredTransportMethod.Train:
                case PreferredTransportMethod.Metro:
                case PreferredTransportMethod.Tram:
                case PreferredTransportMethod.Ferry:
                case PreferredTransportMethod.Airplane:
                    // Allow public transport (specific line filtering happens via cost)
                    methods |= publicTransportMethods;
                    break;
                    
                case PreferredTransportMethod.Taxi:
                    methods |= PathMethod.Taxi;
                    break;
                    
                case PreferredTransportMethod.Walking:
                    // Only pedestrian - already set
                    break;
                    
                case PreferredTransportMethod.Bicycle:
                    methods |= PathMethod.Bicycle | PathMethod.BicycleParking;
                    break;
                    
                case PreferredTransportMethod.Car:
                    methods |= PathMethod.Road | PathMethod.Parking;
                    break;
            }
            
            return methods;
        }
        
        /// <summary>
        /// Get the appropriate public transport methods based on time of day.
        /// Day time: 0.25 (6 AM) to 0.916... (10 PM)
        /// Night time: 0.916... to 0.25 (next day)
        /// </summary>
        private static PathMethod GetPublicTransportMethods(float timeOfDay)
        {
            const float DAY_START = 0.25f;      // 6 AM
            const float DAY_END = 11f / 12f;    // 10 PM (0.916...)
            
            bool isDay = timeOfDay >= DAY_START && timeOfDay < DAY_END;
            
            if (isDay)
            {
                return PathMethod.PublicTransportDay;
            }
            else
            {
                return PathMethod.PublicTransportNight;
            }
        }
    }
}
