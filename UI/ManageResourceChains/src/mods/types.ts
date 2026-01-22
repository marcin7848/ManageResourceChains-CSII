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

export interface TransportPreferences {
    bus: boolean;
    train: boolean;
    tram: boolean;
    metro: boolean;
    ferry: boolean;
    airplane: boolean;
    taxi: boolean;
    walking: boolean;
    bicycle: boolean;
    car: boolean;
}

export interface ResourceChainRule {
    id: string;
    color: string;
    type: ChainType;
    allow: AllowType;
    transportType: TransportType;
    buildings: number[];
    districts: number[];
    transportPreferences: TransportPreferences; // Required field
}

export interface BuildingConfiguration {
    buildingEntityId: number;
    rules: ResourceChainRule[];
}

