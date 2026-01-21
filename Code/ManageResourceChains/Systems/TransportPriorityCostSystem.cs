using System;
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
    /// System that enforces transport preference by:
    /// 1. Setting preferred transport line ticket prices to 0 (free)
    /// 2. Setting non-preferred transport line ticket prices very high
    /// 3. Manipulating comfort factors for stops
    /// 4. Enabling/disabling personal vehicle access (CarKeeper, BicycleOwner)
    /// 
    /// This forces the pathfinding AI to choose the preferred transport.
    /// </summary>
    public partial class TransportPriorityCostSystem : GameSystemBase
    {
        private EntityQuery _allTransportLineQuery;
        private EntityQuery _carKeeperQuery;
        private EntityQuery _bicycleOwnerQuery;
        private EntityQuery _allTransportStopQuery;
        
        // Track original values for restoration
        private Dictionary<Entity, ushort> _originalTicketPrices = new Dictionary<Entity, ushort>();
        private Dictionary<Entity, float> _originalComfortFactors = new Dictionary<Entity, float>();
        private Dictionary<Entity, float> _originalVehicleIntervals = new Dictionary<Entity, float>();
        private Dictionary<Entity, TransportLineFlags> _originalLineFlags = new Dictionary<Entity, TransportLineFlags>();
        
        // Track citizens whose components we disabled
        private HashSet<Entity> _disabledCarKeepers = new HashSet<Entity>();
        private HashSet<Entity> _disabledBicycleOwners = new HashSet<Entity>();
        
        // Track if we've enabled components (for car/bike preference)
        private HashSet<Entity> _enabledCarKeepers = new HashSet<Entity>();
        private HashSet<Entity> _enabledBicycleOwners = new HashSet<Entity>();
        
        // Whether we've applied modifications
        private bool _modificationsApplied = false;
        
        // The penalty to apply to non-preferred transport (higher = less likely to be chosen)
        private const ushort NON_PREFERRED_TICKET_PRICE = 65535; // Maximum possible price (ushort.MaxValue)
        
        // Delegate for checking if a line is of a specific type
        private delegate bool IsLineOfTypeDelegate(Entity lineEntity);
        
        // Delegate for checking if a stop is of a specific type
        private delegate bool IsStopOfTypeDelegate(Entity stopEntity);
        
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Update every 128 frames for more responsive enforcement
            return 128;
        }
        
        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info("TransportPriorityCostSystem created - will enforce transport preferences");
            
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
            
            // Apply preferences based on type
            switch (preference)
            {
                case TransportPreferenceSystem.PreferredTransportMethod.Bus:
                    ApplyPublicTransportPreference("Bus", IsBusLine, IsBusStop);
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Train:
                    ApplyPublicTransportPreference("Train", IsTrainLine, IsTrainStop);
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Tram:
                    ApplyPublicTransportPreference("Tram", IsTramLine, IsTramStop);
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Metro:
                    ApplyPublicTransportPreference("Metro", IsSubwayLine, IsSubwayStop);
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Ferry:
                    ApplyPublicTransportPreference("Ferry", IsShipLine, IsShipStop);
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Airplane:
                    ApplyPublicTransportPreference("Airplane", IsAirplaneLine, IsAirplaneStop);
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.PublicTransport:
                    // Allow all public transport - just disable personal vehicles
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Taxi:
                    // Disable personal vehicles, make all public transport expensive
                    ApplyTaxiPreference();
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Walking:
                    // Disable everything except walking
                    DisableAllPublicTransport();
                    DisablePersonalVehicles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Bicycle:
                    // Enable bicycles, disable everything else
                    DisableAllPublicTransport();
                    DisableCars();
                    EnableBicycles();
                    break;
                    
                case TransportPreferenceSystem.PreferredTransportMethod.Car:
                    // Enable cars, disable everything else
                    DisableAllPublicTransport();
                    DisableBicycles();
                    EnableCars();
                    break;
            }
            
            _modificationsApplied = true;
        }
        
        /// <summary>
        /// Generic method to apply preference for a specific public transport type.
        /// Makes the preferred type free and comfortable, all others expensive and uncomfortable.
        /// </summary>
        private void ApplyPublicTransportPreference(string transportName, IsLineOfTypeDelegate isPreferredLine, IsStopOfTypeDelegate isPreferredStop)
        {
            // Apply to transport lines
            var entities = _allTransportLineQuery.ToEntityArray(Allocator.Temp);
            int preferredLinesModified = 0;
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
                
                bool isPreferred = isPreferredLine(entity);
                
                if (isPreferred)
                {
                    // Make preferred transport FREE
                    if (transportLine.m_TicketPrice != 0)
                    {
                        transportLine.m_TicketPrice = 0;
                        EntityManager.SetComponentData(entity, transportLine);
                        preferredLinesModified++;
                    }
                }
                else
                {
                    // Make non-preferred transport EXPENSIVE and RARE
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
            
            if (preferredLinesModified > 0 || otherLinesDisabled > 0)
            {
                Mod.log.Info($"{transportName} preference: {preferredLinesModified} {transportName.ToLower()} lines free, {otherLinesDisabled} non-{transportName.ToLower()} lines DISABLED");
            }
            
            // Apply to transport stops
            ModifyStopComfort(transportName, isPreferredStop);
        }
        
        /// <summary>
        /// Generic method to modify comfort factors of transport stops.
        /// Preferred stops get maximum comfort, others get minimum.
        /// </summary>
        private void ModifyStopComfort(string transportName, IsStopOfTypeDelegate isPreferredStop)
        {
            var stopEntities = _allTransportStopQuery.ToEntityArray(Allocator.Temp);
            int preferredStopsModified = 0;
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
                
                bool isPreferred = isPreferredStop(stopEntity);
                
                if (isPreferred)
                {
                    // Maximum comfort for preferred stops (1.0 = no penalty)
                    if (stop.m_ComfortFactor < 1.0f)
                    {
                        stop.m_ComfortFactor = 1.0f;
                        stop.m_LoadingFactor = 1.0f; // Fast loading too
                        EntityManager.SetComponentData(stopEntity, stop);
                        preferredStopsModified++;
                    }
                }
                else
                {
                    // Minimum comfort for non-preferred stops (0.01 = huge penalty)
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
            
            if (preferredStopsModified > 0 || otherStopsModified > 0)
            {
                Mod.log.Info($"Modified stop comfort: {preferredStopsModified} {transportName.ToLower()} stops maximized, {otherStopsModified} other stops minimized");
            }
        }
        
        /// <summary>
        /// Disable CarKeeper and BicycleOwner components so citizens can't use personal vehicles.
        /// </summary>
        private void DisablePersonalVehicles()
        {
            DisableCars();
            DisableBicycles();
        }
        
        /// <summary>
        /// Disable CarKeeper component so citizens can't use personal cars.
        /// </summary>
        private void DisableCars()
        {
            int carsDisabled = 0;
            
            var carEntities = _carKeeperQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in carEntities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                
                if (EntityManager.IsComponentEnabled<CarKeeper>(entity))
                {
                    EntityManager.SetComponentEnabled<CarKeeper>(entity, false);
                    _disabledCarKeepers.Add(entity);
                    carsDisabled++;
                }
            }
            carEntities.Dispose();
            
            if (carsDisabled > 0)
            {
                Mod.log.Info($"Disabled cars: {carsDisabled}");
            }
        }
        
        /// <summary>
        /// Disable BicycleOwner component so citizens can't use bicycles.
        /// </summary>
        private void DisableBicycles()
        {
            int bikesDisabled = 0;
            
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
            
            if (bikesDisabled > 0)
            {
                Mod.log.Info($"Disabled bicycles: {bikesDisabled}");
            }
        }
        
        /// <summary>
        /// Enable CarKeeper component to allow personal car use (for Car preference).
        /// </summary>
        private void EnableCars()
        {
            int carsEnabled = 0;
            
            var carEntities = _carKeeperQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in carEntities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                
                if (!EntityManager.IsComponentEnabled<CarKeeper>(entity))
                {
                    EntityManager.SetComponentEnabled<CarKeeper>(entity, true);
                    _enabledCarKeepers.Add(entity);
                    carsEnabled++;
                }
            }
            carEntities.Dispose();
            
            if (carsEnabled > 0)
            {
                Mod.log.Info($"Enabled cars: {carsEnabled}");
            }
        }
        
        /// <summary>
        /// Enable BicycleOwner component to allow bicycle use (for Bicycle preference).
        /// </summary>
        private void EnableBicycles()
        {
            int bikesEnabled = 0;
            
            var bikeEntities = _bicycleOwnerQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in bikeEntities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                
                if (!EntityManager.IsComponentEnabled<BicycleOwner>(entity))
                {
                    EntityManager.SetComponentEnabled<BicycleOwner>(entity, true);
                    _enabledBicycleOwners.Add(entity);
                    bikesEnabled++;
                }
            }
            bikeEntities.Dispose();
            
            if (bikesEnabled > 0)
            {
                Mod.log.Info($"Enabled bicycles: {bikesEnabled}");
            }
        }
        
        /// <summary>
        /// Make all public transport lines expensive (for Taxi, Walking, Bicycle, or Car preference).
        /// </summary>
        private void DisableAllPublicTransport()
        {
            var entities = _allTransportLineQuery.ToEntityArray(Allocator.Temp);
            int linesDisabled = 0;
            
            foreach (var entity in entities)
            {
                if (!EntityManager.Exists(entity))
                    continue;
                    
                var transportLine = EntityManager.GetComponentData<TransportLine>(entity);
                
                if (!_originalTicketPrices.ContainsKey(entity))
                {
                    _originalTicketPrices[entity] = transportLine.m_TicketPrice;
                    _originalVehicleIntervals[entity] = transportLine.m_VehicleInterval;
                    _originalLineFlags[entity] = transportLine.m_Flags;
                }
                
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
                    linesDisabled++;
                }
            }
            
            entities.Dispose();
            
            if (linesDisabled > 0)
            {
                Mod.log.Info($"Disabled all public transport: {linesDisabled} lines");
            }
        }
        
        /// <summary>
        /// Apply taxi preference: make all public transport expensive but keep taxis cheap.
        /// </summary>
        private void ApplyTaxiPreference()
        {
            DisableAllPublicTransport();
            Mod.log.Info("Taxi preference: all public transport disabled");
        }
        
        // ========== Line Detection Methods ==========
        
        private bool IsBusLine(Entity lineEntity)
        {
            return IsLineOfType<BusStop>(lineEntity);
        }
        
        private bool IsTrainLine(Entity lineEntity)
        {
            return IsLineOfType<TrainStop>(lineEntity);
        }
        
        private bool IsTramLine(Entity lineEntity)
        {
            return IsLineOfType<TramStop>(lineEntity);
        }
        
        private bool IsSubwayLine(Entity lineEntity)
        {
            return IsLineOfType<SubwayStop>(lineEntity);
        }
        
        private bool IsShipLine(Entity lineEntity)
        {
            return IsLineOfType<ShipStop>(lineEntity);
        }
        
        private bool IsAirplaneLine(Entity lineEntity)
        {
            return IsLineOfType<AirplaneStop>(lineEntity);
        }
        
        /// <summary>
        /// Generic method to check if a transport line uses stops of a specific type.
        /// </summary>
        private bool IsLineOfType<TStopComponent>(Entity lineEntity) where TStopComponent : struct, IComponentData
        {
            if (!EntityManager.HasBuffer<RouteWaypoint>(lineEntity))
                return false;
                
            var waypoints = EntityManager.GetBuffer<RouteWaypoint>(lineEntity);
            
            foreach (var waypoint in waypoints)
            {
                Entity waypointEntity = waypoint.m_Waypoint;
                if (EntityManager.Exists(waypointEntity))
                {
                    if (EntityManager.HasComponent<TStopComponent>(waypointEntity))
                        return true;
                    
                    // Also check Connected entities
                    if (EntityManager.HasComponent<Connected>(waypointEntity))
                    {
                        var connected = EntityManager.GetComponentData<Connected>(waypointEntity);
                        if (EntityManager.Exists(connected.m_Connected) && 
                            EntityManager.HasComponent<TStopComponent>(connected.m_Connected))
                            return true;
                    }
                }
            }
            
            return false;
        }
        
        // ========== Stop Detection Methods ==========
        
        private bool IsBusStop(Entity stopEntity)
        {
            return EntityManager.HasComponent<BusStop>(stopEntity);
        }
        
        private bool IsTrainStop(Entity stopEntity)
        {
            return EntityManager.HasComponent<TrainStop>(stopEntity);
        }
        
        private bool IsTramStop(Entity stopEntity)
        {
            return EntityManager.HasComponent<TramStop>(stopEntity);
        }
        
        private bool IsSubwayStop(Entity stopEntity)
        {
            return EntityManager.HasComponent<SubwayStop>(stopEntity);
        }
        
        private bool IsShipStop(Entity stopEntity)
        {
            return EntityManager.HasComponent<ShipStop>(stopEntity);
        }
        
        private bool IsAirplaneStop(Entity stopEntity)
        {
            return EntityManager.HasComponent<AirplaneStop>(stopEntity);
        }
        
        // ========== Restoration ==========
        
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
            
            // Re-enable CarKeepers that we disabled
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
            
            // Re-enable BicycleOwners that we disabled
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
            
            // Note: We don't need to disable cars/bikes that we enabled,
            // since the vanilla game should manage their enabled state
            _enabledCarKeepers.Clear();
            _enabledBicycleOwners.Clear();
            
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

