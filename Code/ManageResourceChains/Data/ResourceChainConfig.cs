﻿using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ManageResourceChains.Data
{
    /// <summary>
    /// Represents a single resource chain rule configuration
    /// </summary>
    [Serializable]
    public class ResourceChainRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Color { get; set; } = "#FF0000";
        public ChainType Type { get; set; } = ChainType.Incoming;
        public AllowType Allow { get; set; } = AllowType.Allow;
        public TransportType TransportType { get; set; } = TransportType.Resources;
        public List<int> Buildings { get; set; } = new List<int>();
        public List<int> Districts { get; set; } = new List<int>();
        public List<TransportPriority> TransportPriorities { get; set; } = new List<TransportPriority>();
        
        // Additional granular filters
        public List<int> SpecificResources { get; set; } = new List<int>(); // Resource indices to filter (empty = all)
        public List<int> WorkerEducationLevels { get; set; } = new List<int>(); // 0-4 education levels (empty = all)
        
        /// <summary>
        /// Check if transport is allowed from/to a specific building based on this rule
        /// </summary>
        /// <param name="buildingEntity">The building entity to check</param>
        /// <param name="isSource">True if this building is the source, false if destination</param>
        /// <returns>True if transport is allowed</returns>
        public bool IsTransportAllowed(int buildingEntity, bool isSource)
        {
            // Check if this building is affected by the rule
            bool isAffected = Buildings.Contains(buildingEntity);
            
            if (!isAffected)
                return true; // Not affected by this rule
            
            // Check direction
            bool matchesDirection = (Type == ChainType.Incoming && !isSource) || 
                                   (Type == ChainType.Outgoing && isSource);
            
            if (!matchesDirection)
                return true; // Direction doesn't match
            
            // Return based on Allow/Disallow
            return Allow == AllowType.Allow;
        }
    }

    [Serializable]
    public class TransportPriority
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public TransportStationType StationType { get; set; } = TransportStationType.TrainStation;
        public int StationEntity { get; set; } = 0;
        public int Priority { get; set; } = 1;
    }


    public enum ChainType
    {
        Incoming,
        Outgoing
    }

    public enum AllowType
    {
        Allow,
        Disallow
    }

    public enum TransportType
    {
        Workers,
        Services,
        Resources
    }

    /// <summary>
    /// Specific type of service for more granular control
    /// </summary>
    public enum ServiceType
    {
        All,            // Applies to all services
        Healthcare,     // Hospitals, ambulances, medical helicopters
        Deathcare,      // Cemeteries, crematoriums, hearses
        Fire,           // Fire stations, fire engines, fire helicopters
        Police,         // Police stations, police cars, police helicopters
        Garbage,        // Garbage facilities, garbage trucks
        Post,           // Post offices, post vans
        Evacuation,     // Emergency shelters, evacuation buses
        Maintenance     // Maintenance depots, maintenance vehicles
    }

    /// <summary>
    /// Specific type of resource for more granular control
    /// </summary>
    public enum ResourceType
    {
        All,                // Applies to all resources
        RawMaterials,       // Grain, livestock, vegetables, wood, stone, coal, oil, ore
        ProcessedGoods,     // Food, textiles, paper, metals, plastics, electronics
        CommercialGoods,    // Retail goods
        Electricity,        // Power distribution
        Water              // Water and sewage
    }

    public enum TransportStationType
    {
        TrainStation,
        Airport,
        Port,
        BusStation,
        SubwayStation,
        TramStation,
        BusStop,
        TramStop,
        FerryTerminal,
        CargoTerminal,
        TaxiStand
    }

    /// <summary>
    /// Type of entity that the configuration applies to
    /// </summary>
    public enum EntityType
    {
        Building,
        District
    }

    /// <summary>
    /// Component data that will be attached to building entities to store their resource chain configuration
    /// </summary>
    public struct ResourceChainData : IComponentData, ISerializable
    {
        // We'll store a reference to the configuration in a lookup table
        // since IComponentData should be blittable for performance
        public int ConfigurationId;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(ConfigurationId);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out ConfigurationId);
        }
    }

    /// <summary>
    /// Component data for tracking multi-stage forced trips
    /// </summary>
    public struct ForcedPriorityTrip : IComponentData, ISerializable
    {
        public Entity m_FinalTarget;
        public Entity m_CurrentResolvedWaypoint;
        public Entity m_CurrentResolvedStop;
        public int m_NextPriorityIndex;
        public ForcedTripState m_State;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(2); // Version
            writer.Write(m_FinalTarget);
            writer.Write(m_CurrentResolvedWaypoint);
            writer.Write(m_CurrentResolvedStop);
            writer.Write(m_NextPriorityIndex);
            writer.Write((int)m_State);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            
            if (version == 2)
            {
                reader.Read(out m_FinalTarget);
                reader.Read(out m_CurrentResolvedWaypoint);
                reader.Read(out m_CurrentResolvedStop);
                reader.Read(out m_NextPriorityIndex);
                reader.Read(out int state);
                m_State = (ForcedTripState)state;
            }
            else if (version == 1)
            {
                reader.Read(out m_FinalTarget);
                reader.Read(out m_NextPriorityIndex);
                reader.Read(out int state);
                m_State = (ForcedTripState)state;
                m_CurrentResolvedWaypoint = Entity.Null;
                m_CurrentResolvedStop = Entity.Null;
            }
            else
            {
                // Old v0 save (no version header). The first 4 bytes read was m_FinalTarget.Index.
                // An Entity is 8 bytes (Index + Version).
                reader.Read(out int finalTargetVersion);
                reader.Read(out int nextPriorityIndex);
                reader.Read(out int state);
                
                m_FinalTarget = Entity.Null; // Reset to safe state
                m_CurrentResolvedWaypoint = Entity.Null;
                m_CurrentResolvedStop = Entity.Null;
                m_NextPriorityIndex = 0;
                m_State = ForcedTripState.MovingToPriority;
            }
        }
    }

    public enum ForcedTripState
    {
        MovingToPriority,
        WaitingAtPriority,
        MovingToFinalTarget
    }

    /// <summary>
    /// Storage for entity configurations (buildings or districts)
    /// </summary>
    public class BuildingConfiguration
    {
        public int BuildingEntityId { get; set; }
        public EntityType Type { get; set; } = EntityType.Building;
        public List<ResourceChainRule> Rules { get; set; } = new List<ResourceChainRule>();
    }
}

