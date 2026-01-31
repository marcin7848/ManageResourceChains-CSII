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

export enum PreferredTransportType {
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

export enum EntityType {
    Building = 0,
    District = 1
}

export interface ResourceChainRule {
    id: string;
    color: string;
    type: ChainType;
    allow: AllowType;
    transportType: TransportType;
    buildings: number[];
    districts: number[];
}

export interface BuildingConfiguration {
    buildingEntityId: number;
    type: EntityType;
    rules: ResourceChainRule[];
}

