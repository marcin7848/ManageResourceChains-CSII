// Type definitions for resource chain management

export enum ChainType {
    Incoming = 0,
    Outgoing = 1
}

export enum AllowType {
    Allow = 0,
    Disallow = 1
}

export enum TransportType {
    Workers = 0,
    Services = 1,
    Resources = 2
}

export enum TransportStationType {
    TrainStation = 0,
    Airport = 1,
    Port = 2,
    BusStation = 3,
    SubwayStation = 4,
    TramStation = 5,
    BusStop = 6,
    TramStop = 7,
    FerryTerminal = 8,
    CargoTerminal = 9,
    TaxiStand = 10
}

export interface TransportPriority {
    id: string;
    stationType: TransportStationType;
    stationEntity: number;
    priority: number;
}

export interface ResourceChainRule {
    id: string;
    color: string;
    type: ChainType;
    allow: AllowType;
    transportType: TransportType;
    buildings: number[];
    districts: number[];
    transportPriorities: TransportPriority[];
}

export interface BuildingConfiguration {
    buildingEntityId: number;
    rules: ResourceChainRule[];
}

