using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Game;
using Game.Routes;
using Game.Common;
using Game.Citizens;

namespace ManageResourceChains.Systems
{
    /// <summary>
    /// System that enforces bus transport preference by:
    /// 1. Setting bus line ticket prices to 0 (free)
    /// 2. Setting non-bus line ticket prices very high
    /// 3. Disabling CarKeeper component on citizens so they can't use personal cars
    /// 
    /// This forces the pathfinding AI to choose bus transport.
    /// </summary>
    public partial class TransportPriorityCostSystem : GameSystemBase
    {
        private EntityQuery _allTransportLineQuery;
        private EntityQuery _carKeeperQuery;
        private EntityQuery _bicycleOwnerQuery;
        private EntityQuery _allTransportStopQuery;
        
        // Track original ticket prices for restoration
        private Dictionary<Entity, ushort> _originalTicketPrices = new Dictionary<Entity, ushort>();
        
        // Track original stop comfort factors
        private Dictionary<Entity, float> _originalComfortFactors = new Dictionary<Entity, float>();
        
        // Track original vehicle intervals
        private Dictionary<Entity, float> _originalVehicleIntervals = new Dictionary<Entity, float>();
        
        // Track original line flags
        private Dictionary<Entity, TransportLineFlags> _originalLineFlags = new Dictionary<Entity, TransportLineFlags>();
        
        // Track citizens whose CarKeeper we disabled
        private HashSet<Entity> _disabledCarKeepers = new HashSet<Entity>();
        private HashSet<Entity> _disabledBicycleOwners = new HashSet<Entity>();
        
        // Whether we've applied modifications
        private bool _modificationsApplied = false;
        
        // The penalty to apply to non-preferred transport (higher = less likely to be chosen)
        private const ushort NON_PREFERRED_TICKET_PRICE = 65535; // Maximum possible price (ushort.MaxValue)
        
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Update every 128 frames for more responsive enforcement
            return 128;
        }
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("TransportPriorityCostSystem created - will FORCE bus transport");
            
            _allTransportLineQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<TransportLine>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            
            // Query for citizens with enabled CarKeeper
            _carKeeperQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadWrite<CarKeeper>()
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            
            // Query for citizens with enabled BicycleOwner
            _bicycleOwnerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { 
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadWrite<BicycleOwner>()
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
            
            // Query for all transport stops to track comfort factors
            _allTransportStopQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TransportStop>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            var preference = TransportPreferenceSystem.DefaultPreference;
            
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.None)
            {
                if (_modificationsApplied)
                {
                    RestoreAll();
                    _modificationsApplied = false;
                }
                return;
            }
            
            if (preference == TransportPreferenceSystem.PreferredTransportMethod.Bus ||
                preference == TransportPreferenceSystem.PreferredTransportMethod.PublicTransport)
            {
                ApplyBusPreference();
                ModifyStopComfort();
                DisablePersonalVehicles();
                _modificationsApplied = true;
            }
            else if (preference == TransportPreferenceSystem.PreferredTransportMethod.Train)
            {
                ApplyTrainPreference();
                ModifyStopComfortForTrain();
                DisablePersonalVehicles();
                _modificationsApplied = true;
            }
        }
        
        private void ApplyBusPreference()
        {
            var entities = _allTransportLineQuery.ToEntityArray(Allocator.Temp);
            int busLinesModified = 0;
            int otherLinesDisabled = 0;
            
            foreach (var entity in entities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                    
                var transportLine = EntityManager.GetComponentData<TransportLine>(entity);
                
                // Store originals if not already stored
                if (!_originalTicketPrices.ContainsKey(entity))
                {
                    _originalTicketPrices[entity] = transportLine.m_TicketPrice;
                    _originalVehicleIntervals[entity] = transportLine.m_VehicleInterval;
                    _originalLineFlags[entity] = transportLine.m_Flags;
                }
                
                bool isBusLine = IsBusLine(entity);
                
                if (isBusLine)
                {
                    // Make bus free
                    if (transportLine.m_TicketPrice != 0)
                    {
                        transportLine.m_TicketPrice = 0;
                        EntityManager.SetComponentData(entity, transportLine);
                        busLinesModified++;
                    }
                }
                else
                {
                    // COMPLETELY DISABLE non-bus lines by:
                    // 1. Setting vehicle interval to max (no vehicles spawn)
                    // 2. Setting ticket price to max
                    bool modified = false;
                    
                    if (transportLine.m_VehicleInterval < 10000f)
                    {
                        transportLine.m_VehicleInterval = 10000f; // Vehicles almost never spawn
                        modified = true;
                    }
                    
                    if (transportLine.m_TicketPrice < NON_PREFERRED_TICKET_PRICE)
                    {
                        transportLine.m_TicketPrice = NON_PREFERRED_TICKET_PRICE;
                        modified = true;
                    }
                    
                    if (modified)
                    {
                        EntityManager.SetComponentData(entity, transportLine);
                        otherLinesDisabled++;
                    }
                }
            }
            
            entities.Dispose();
            
            if (busLinesModified > 0 || otherLinesDisabled > 0)
            {
                Mod.log.Info($"Bus preference: {busLinesModified} bus lines free, {otherLinesDisabled} non-bus lines DISABLED");
            }
        }
        
        /// <summary>
        /// Disable CarKeeper and BicycleOwner components so citizens can't use personal vehicles.
        /// This forces them to use public transport or walk.
        /// </summary>
        private void DisablePersonalVehicles()
        {
            int carsDisabled = 0;
            int bikesDisabled = 0;
            
            // Disable car access
            var carEntities = _carKeeperQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in carEntities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                
                // Check if CarKeeper is enabled
                if (EntityManager.IsComponentEnabled<CarKeeper>(entity))
                {
                    // Disable it so pathfinding won't consider the car
                    EntityManager.SetComponentEnabled<CarKeeper>(entity, false);
                    _disabledCarKeepers.Add(entity);
                    carsDisabled++;
                }
            }
            carEntities.Dispose();
            
            // Disable bicycle access
            var bikeEntities = _bicycleOwnerQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in bikeEntities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                
                if (EntityManager.IsComponentEnabled<BicycleOwner>(entity))
                {
                    EntityManager.SetComponentEnabled<BicycleOwner>(entity, false);
                    _disabledBicycleOwners.Add(entity);
                    bikesDisabled++;
                }
            }
            bikeEntities.Dispose();
            
            if (carsDisabled > 0 || bikesDisabled > 0)
            {
                Mod.log.Info($"Disabled personal vehicles: {carsDisabled} cars, {bikesDisabled} bicycles");
            }
        }
        
        /// <summary>
        /// Modify comfort factors of transport stops to heavily favor bus stops.
        /// High comfort = low cost in pathfinding.
        /// </summary>
        private void ModifyStopComfort()
        {
            var stopEntities = _allTransportStopQuery.ToEntityArray(Allocator.Temp);
            int busStopsModified = 0;
            int otherStopsModified = 0;
            
            foreach (var stopEntity in stopEntities)
            {
                if (!EntityManager.Exists(stopEntity) || !EntityManager.HasComponent<TransportStop>(stopEntity))
                    continue;
                
                var stop = EntityManager.GetComponentData<TransportStop>(stopEntity);
                
                // Store original comfort if not already stored
                if (!_originalComfortFactors.ContainsKey(stopEntity))
                {
                    _originalComfortFactors[stopEntity] = stop.m_ComfortFactor;
                }
                
                // Check if this is a bus stop
                bool isBusStop = EntityManager.HasComponent<BusStop>(stopEntity);
                
                if (isBusStop)
                {
                    // Maximum comfort for bus stops (1.0 = no penalty)
                    if (stop.m_ComfortFactor < 1.0f)
                    {
                        stop.m_ComfortFactor = 1.0f;
                        stop.m_LoadingFactor = 1.0f; // Fast loading too
                        EntityManager.SetComponentData(stopEntity, stop);
                        busStopsModified++;
                    }
                }
                else
                {
                    // Minimum comfort for non-bus stops (0.01 = huge penalty)
                    if (stop.m_ComfortFactor > 0.01f)
                    {
                        stop.m_ComfortFactor = 0.01f;
                        stop.m_LoadingFactor = 0.01f; // Slow loading too
                        EntityManager.SetComponentData(stopEntity, stop);
                        otherStopsModified++;
                    }
                }
            }
            
            stopEntities.Dispose();
            
            if (busStopsModified > 0 || otherStopsModified > 0)
            {
                Mod.log.Info($"Modified stop comfort: {busStopsModified} bus stops maximized, {otherStopsModified} other stops minimized");
            }
        }
        
        private bool IsBusLine(Entity lineEntity)
        {
            if (!EntityManager.HasBuffer<RouteWaypoint>(lineEntity))
                return false;
                
            var waypoints = EntityManager.GetBuffer<RouteWaypoint>(lineEntity);
            
            foreach (var waypoint in waypoints)
            {
                Entity waypointEntity = waypoint.m_Waypoint;
                if (EntityManager.Exists(waypointEntity))
                {
                    if (EntityManager.HasComponent<BusStop>(waypointEntity))
                    {
                        return true;
                    }
                    
                    if (EntityManager.HasComponent<Connected>(waypointEntity))
                    {
                        var connected = EntityManager.GetComponentData<Connected>(waypointEntity);
                        if (EntityManager.Exists(connected.m_Connected) && 
                            EntityManager.HasComponent<BusStop>(connected.m_Connected))
                        {
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }
        
        private bool IsTrainLine(Entity lineEntity)
        {
            if (!EntityManager.HasBuffer<RouteWaypoint>(lineEntity))
            {
                Mod.log.Info($"Line {lineEntity.Index} has no RouteWaypoint buffer");
                return false;
            }
                
            var waypoints = EntityManager.GetBuffer<RouteWaypoint>(lineEntity);
            Mod.log.Info($"Checking line {lineEntity.Index}: {waypoints.Length} waypoints");
            
            foreach (var waypoint in waypoints)
            {
                Entity waypointEntity = waypoint.m_Waypoint;
                if (EntityManager.Exists(waypointEntity))
                {
                    // Log what components this waypoint has
                    bool hasTrainStop = EntityManager.HasComponent<TrainStop>(waypointEntity);
                    bool hasBusStop = EntityManager.HasComponent<BusStop>(waypointEntity);
                    bool hasTramStop = EntityManager.HasComponent<TramStop>(waypointEntity);
                    bool hasSubwayStop = EntityManager.HasComponent<SubwayStop>(waypointEntity);
                    bool hasTransportStop = EntityManager.HasComponent<TransportStop>(waypointEntity);
                    
                    Mod.log.Info($"  Waypoint {waypointEntity.Index}: Train={hasTrainStop}, Bus={hasBusStop}, Tram={hasTramStop}, Subway={hasSubwayStop}, TransportStop={hasTransportStop}");
                    
                    // Check for TrainStop component
                    if (hasTrainStop)
                    {
                        Mod.log.Info($"  -> Found TrainStop! Line is a train line");
                        return true;
                    }
                    
                    if (EntityManager.HasComponent<Connected>(waypointEntity))
                    {
                        var connected = EntityManager.GetComponentData<Connected>(waypointEntity);
                        if (EntityManager.Exists(connected.m_Connected))
                        {
                            bool connectedHasTrainStop = EntityManager.HasComponent<TrainStop>(connected.m_Connected);
                            Mod.log.Info($"  Connected {connected.m_Connected.Index}: TrainStop={connectedHasTrainStop}");
                            
                            if (connectedHasTrainStop)
                            {
                                Mod.log.Info($"  -> Found TrainStop in connected! Line is a train line");
                                return true;
                            }
                        }
                    }
                }
            }
            
            Mod.log.Info($"Line {lineEntity.Index}: NOT a train line");
            return false;
        }
        
        private void ApplyTrainPreference()
        {
            var entities = _allTransportLineQuery.ToEntityArray(Allocator.Temp);
            int trainLinesModified = 0;
            int otherLinesDisabled = 0;
            
            foreach (var entity in entities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                    
                var transportLine = EntityManager.GetComponentData<TransportLine>(entity);
                
                // Store originals if not already stored
                if (!_originalTicketPrices.ContainsKey(entity))
                {
                    _originalTicketPrices[entity] = transportLine.m_TicketPrice;
                    _originalVehicleIntervals[entity] = transportLine.m_VehicleInterval;
                    _originalLineFlags[entity] = transportLine.m_Flags;
                }
                
                bool isTrainLine = IsTrainLine(entity);
                
                if (isTrainLine)
                {
                    // Make train FREE (opposite of bus preference)
                    if (transportLine.m_TicketPrice != 0)
                    {
                        transportLine.m_TicketPrice = 0;
                        EntityManager.SetComponentData(entity, transportLine);
                        trainLinesModified++;
                    }
                }
                else
                {
                    // Make non-train transport EXPENSIVE
                    bool modified = false;
                    
                    if (transportLine.m_VehicleInterval < 10000f)
                    {
                        transportLine.m_VehicleInterval = 10000f;
                        modified = true;
                    }
                    
                    if (transportLine.m_TicketPrice < NON_PREFERRED_TICKET_PRICE)
                    {
                        transportLine.m_TicketPrice = NON_PREFERRED_TICKET_PRICE;
                        modified = true;
                    }
                    
                    if (modified)
                    {
                        EntityManager.SetComponentData(entity, transportLine);
                        otherLinesDisabled++;
                    }
                }
            }
            
            entities.Dispose();
            
            if (trainLinesModified > 0 || otherLinesDisabled > 0)
            {
                Mod.log.Info($"Train preference: {trainLinesModified} train lines free, {otherLinesDisabled} non-train lines DISABLED");
            }
        }
        
        private void ModifyStopComfortForTrain()
        {
            var stopEntities = _allTransportStopQuery.ToEntityArray(Allocator.Temp);
            int trainStopsModified = 0;
            int otherStopsModified = 0;
            
            foreach (var stopEntity in stopEntities)
            {
                if (!EntityManager.Exists(stopEntity) || !EntityManager.HasComponent<TransportStop>(stopEntity))
                    continue;
                
                var stop = EntityManager.GetComponentData<TransportStop>(stopEntity);
                
                // Store original comfort if not already stored
                if (!_originalComfortFactors.ContainsKey(stopEntity))
                {
                    _originalComfortFactors[stopEntity] = stop.m_ComfortFactor;
                }
                
                // Check if this is a train stop
                bool isTrainStop = EntityManager.HasComponent<TrainStop>(stopEntity);
                
                if (isTrainStop)
                {
                    // Maximum comfort for train stops
                    if (stop.m_ComfortFactor < 1.0f)
                    {
                        stop.m_ComfortFactor = 1.0f;
                        stop.m_LoadingFactor = 1.0f;
                        EntityManager.SetComponentData(stopEntity, stop);
                        trainStopsModified++;
                    }
                }
                else
                {
                    // Minimum comfort for non-train stops
                    if (stop.m_ComfortFactor > 0.01f)
                    {
                        stop.m_ComfortFactor = 0.01f;
                        stop.m_LoadingFactor = 0.01f;
                        EntityManager.SetComponentData(stopEntity, stop);
                        otherStopsModified++;
                    }
                }
            }
            
            stopEntities.Dispose();
            
            if (trainStopsModified > 0 || otherStopsModified > 0)
            {
                Mod.log.Info($"Modified stop comfort: {trainStopsModified} train stops maximized, {otherStopsModified} other stops minimized");
            }
        }
        
        private void RestoreAll()
        {
            // Restore ticket prices, vehicle intervals, and flags
            int pricesRestored = 0;
            foreach (var kvp in _originalTicketPrices)
            {
                Entity entity = kvp.Key;
                
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<TransportLine>(entity))
                {
                    var transportLine = EntityManager.GetComponentData<TransportLine>(entity);
                    transportLine.m_TicketPrice = kvp.Value;
                    
                    if (_originalVehicleIntervals.ContainsKey(entity))
                    {
                        transportLine.m_VehicleInterval = _originalVehicleIntervals[entity];
                    }
                    
                    if (_originalLineFlags.ContainsKey(entity))
                    {
                        transportLine.m_Flags = _originalLineFlags[entity];
                    }
                    
                    EntityManager.SetComponentData(entity, transportLine);
                    pricesRestored++;
                }
            }
            _originalTicketPrices.Clear();
            _originalVehicleIntervals.Clear();
            _originalLineFlags.Clear();
            
            // Restore comfort factors
            int comfortRestored = 0;
            foreach (var kvp in _originalComfortFactors)
            {
                Entity entity = kvp.Key;
                float originalComfort = kvp.Value;
                
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<TransportStop>(entity))
                {
                    var stop = EntityManager.GetComponentData<TransportStop>(entity);
                    stop.m_ComfortFactor = originalComfort;
                    EntityManager.SetComponentData(entity, stop);
                    comfortRestored++;
                }
            }
            _originalComfortFactors.Clear();
            
            // Re-enable CarKeepers
            int carsRestored = 0;
            foreach (var entity in _disabledCarKeepers)
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<CarKeeper>(entity))
                {
                    EntityManager.SetComponentEnabled<CarKeeper>(entity, true);
                    carsRestored++;
                }
            }
            _disabledCarKeepers.Clear();
            
            // Re-enable BicycleOwners
            int bikesRestored = 0;
            foreach (var entity in _disabledBicycleOwners)
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<BicycleOwner>(entity))
                {
                    EntityManager.SetComponentEnabled<BicycleOwner>(entity, true);
                    bikesRestored++;
                }
            }
            _disabledBicycleOwners.Clear();
            
            if (pricesRestored > 0 || comfortRestored > 0 || carsRestored > 0 || bikesRestored > 0)
            {
                Mod.log.Info($"Restored: {pricesRestored} prices, {comfortRestored} comfort, {carsRestored} cars, {bikesRestored} bikes");
            }
        }
        
        protected override void OnDestroy()
        {
            RestoreAll();
            base.OnDestroy();
        }
    }
}

