import React, { useState, useEffect } from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { bindValue, trigger } from "cs2/api";
import { getModule } from "cs2/modding";
import { Dropdown, DropdownToggle, Panel, Scrollable } from "cs2/ui";
import { Color } from "cs2/bindings";

// Get the DescriptionTooltip component for proper tooltips
const DescriptionTooltip = getModule("game-ui/common/tooltip/description-tooltip/description-tooltip.tsx", "DescriptionTooltip");

// Get DropdownItem dynamically to avoid TypeScript type/value confusion
// @ts-ignore
const UI = require("cs2/ui");
const DropdownItem = UI.DropdownItem || UI.DropdownItem$1;

// Get the game's ColorField component (color picker with RGB sliders)
const ColorFieldModule = getModule("game-ui/common/input/color-picker/color-field/color-field.tsx", "ColorField");
const FOCUS_DISABLED = getModule("game-ui/common/focus/focus-key.ts", "FOCUS_DISABLED");
import { 
    ResourceChainRule, 
    BuildingConfiguration, 
    ChainType, 
    AllowType, 
    TransportType,
    TransportStationType
} from "./types";

// Import game UI styles like CompanyBrandChanger does
const styleDropdown = getModule("game-ui/menu/themes/dropdown.module.scss", "classes");

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);
const resourceChainConfig$ = bindValue<string>("manageResourceChains", "resourceChainConfig", "{}");
const buildingPickerActive$ = bindValue<boolean>("manageResourceChains", "buildingPickerActive", false);

// District bindings
const isDistrictSelected$ = bindValue<boolean>("manageResourceChains", "isDistrictSelected", false);
const selectedDistrictEntity$ = bindValue<number>("manageResourceChains", "selectedDistrictEntity", 0);
const districtConfig$ = bindValue<string>("manageResourceChains", "districtConfig", "{}");

const BUTTON_CONTAINER_ID = 'manage-resource-chains-container';
const DISTRICT_BUTTON_CONTAINER_ID = 'manage-resource-chains-district-container';
const ACTIONS_SECTION_CLASS = '.actions-section_X1x';

// Helper functions to convert between hex colors and Color objects
function hexToColor(hex: string): Color {
    // Remove # if present
    hex = hex.replace('#', '');
    
    // Parse RGB values
    const r = parseInt(hex.substring(0, 2), 16) / 255;
    const g = parseInt(hex.substring(2, 4), 16) / 255;
    const b = parseInt(hex.substring(4, 6), 16) / 255;
    
    return { r, g, b, a: 1 }; // Alpha always 1 for now
}

function colorToHex(color: Color): string {
    const r = Math.round(color.r * 255).toString(16).padStart(2, '0');
    const g = Math.round(color.g * 255).toString(16).padStart(2, '0');
    const b = Math.round(color.b * 255).toString(16).padStart(2, '0');
    return `#${r}${g}${b}`;
}

// Component with inline controls and two-column layout below
const ResourceChainRuleComponent: React.FC<{
    rule: ResourceChainRule;
    entityId: number;
    fullConfig: BuildingConfiguration; // Add full config
    onUpdate: (rule: ResourceChainRule) => void;
    onDelete: () => void;
    isDistrict?: boolean;  // Optional: true if this is for a district
}> = ({ rule, entityId, fullConfig, onUpdate, onDelete, isDistrict = false }) => {
    const buildingPickerActive = useValue(buildingPickerActive$);
    
    const startBuildingPicker = () => {
        // Only start picking if not already picking
        if (!buildingPickerActive) {
            // Save current config to C# memory (not to disk) so the rule exists there
            // This is required for OnBuildingSelected to work
            const json = JSON.stringify(fullConfig);
            const saveType = isDistrict ? "saveDistrictConfig" : "saveBuildingConfig";
            trigger("manageResourceChains", saveType, entityId, json);
            
            // Small delay to ensure config is updated before starting picker
            setTimeout(() => {
                trigger("manageResourceChains", "startBuildingPicker", entityId, rule.id, isDistrict);
            }, 50);
        }
    };
    
    return (
        <div style={{ 
            padding: '10rem', 
            marginBottom: '10rem', 
            border: '1px solid rgba(255,255,255,0.2)',
            borderRadius: '4rem',
            backgroundColor: 'rgba(0,0,0,0.2)'
        }}>
            {/* Top row: Color picker + 3 dropdowns + Delete button */}
            <div style={{ 
                display: 'flex', 
                alignItems: 'center', 
                gap: '12rem',
                marginBottom: '10rem'
            }}>
                {/* Color picker with RGB sliders - square */}
                <div style={{ 
                    flexShrink: 0,
                    width: '20rem',
                    height: '20rem',
                    overflow: 'hidden',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginRight: '4rem'
                }}>
                    <div style={{
                        width: '20rem',
                        height: '20rem'
                    }}>
                        <ColorFieldModule
                            value={hexToColor(rule?.color || '#FF0000')}
                            focusKey={FOCUS_DISABLED}
                            onChange={(newColor: Color) => {
                                onUpdate({ ...rule, color: colorToHex(newColor) });
                            }}
                            alpha={false}
                        />
                    </div>
                </div>
                
                {/* Type Dropdown */}
                <div style={{ flex: '1', minWidth: '100rem', marginRight: '4rem' }}>
                    <Dropdown
                        theme={styleDropdown}
                        content={[
                            <DropdownItem
                                key="incoming"
                                theme={styleDropdown}
                                value={ChainType.Incoming}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, type: ChainType.Incoming })}
                            >
                                Incoming
                            </DropdownItem>,
                            <DropdownItem
                                key="outgoing"
                                theme={styleDropdown}
                                value={ChainType.Outgoing}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, type: ChainType.Outgoing })}
                            >
                                Outgoing
                            </DropdownItem>
                        ]}
                    >
                        <DropdownToggle>
                            {rule.type === ChainType.Incoming ? 'Incoming' : 'Outgoing'}
                        </DropdownToggle>
                    </Dropdown>
                </div>
                
                {/* Allow Dropdown */}
                <div style={{ flex: '1', minWidth: '90rem', marginRight: '4rem' }}>
                    <Dropdown
                        theme={styleDropdown}
                        content={[
                            <DropdownItem
                                key="allow"
                                theme={styleDropdown}
                                value={AllowType.Allow}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, allow: AllowType.Allow })}
                            >
                                Allow
                            </DropdownItem>,
                            <DropdownItem
                                key="disallow"
                                theme={styleDropdown}
                                value={AllowType.Disallow}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, allow: AllowType.Disallow })}
                            >
                                Disallow
                            </DropdownItem>
                        ]}
                    >
                        <DropdownToggle>
                            {rule.allow === AllowType.Allow ? 'Allow' : 'Disallow'}
                        </DropdownToggle>
                    </Dropdown>
                </div>
                
                {/* Transport Dropdown */}
                <div style={{ flex: '1', minWidth: '100rem', marginRight: '4rem' }}>
                    <Dropdown
                        theme={styleDropdown}
                        content={[
                            <DropdownItem
                                key="workers"
                                theme={styleDropdown}
                                value={TransportType.Workers}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, transportType: TransportType.Workers })}
                            >
                                Workers
                            </DropdownItem>,
                            <DropdownItem
                                key="services"
                                theme={styleDropdown}
                                value={TransportType.Services}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, transportType: TransportType.Services })}
                            >
                                Services
                            </DropdownItem>,
                            <DropdownItem
                                key="resources"
                                theme={styleDropdown}
                                value={TransportType.Resources}
                                closeOnSelect={true}
                                onChange={() => onUpdate({ ...rule, transportType: TransportType.Resources })}
                            >
                                Resources
                            </DropdownItem>
                        ]}
                    >
                        <DropdownToggle>
                            {rule.transportType === TransportType.Workers ? 'Workers' :
                             rule.transportType === TransportType.Services ? 'Services' :
                             'Resources'}
                        </DropdownToggle>
                    </Dropdown>
                </div>
                
                {/* Delete button with XClose icon */}
                <button
                    onClick={onDelete}
                    style={{
                        padding: '4rem',
                        backgroundColor: 'transparent',
                        border: 'none',
                        cursor: 'pointer',
                        flexShrink: 0,
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center'
                    }}
                    title="Delete rule"
                >
                    <img
                        src="coui://uil/Colored/XClose.svg"
                        style={{
                            width: '20rem',
                            height: '20rem'
                        }}
                        alt="Delete"
                    />
                </button>
            </div>
            
            {/* Two-column layout below */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10rem' }}>
                {/* Left column: Buildings/Districts */}
                <div style={{ 
                    padding: '8rem',
                    backgroundColor: 'rgba(255,255,255,0.05)',
                    borderRadius: '3rem'
                }}>
                    <div style={{ 
                        display: 'flex', 
                        justifyContent: 'space-between', 
                        alignItems: 'center',
                        marginBottom: '8rem',
                        fontSize: '12rem',
                        fontWeight: 'bold'
                    }}>
                        <span>Buildings / Districts</span>
                        <div style={{ display: 'flex', gap: '8rem' }}>
                            <button
                                onClick={startBuildingPicker}
                                disabled={buildingPickerActive}
                                style={{
                                    padding: '2rem 6rem',
                                    backgroundColor: buildingPickerActive ? 'rgba(255,165,0,0.5)' : 'rgba(0,150,255,0.3)',
                                    border: `1px solid ${buildingPickerActive ? 'rgba(255,165,0,0.8)' : 'rgba(0,150,255,0.5)'}`,
                                    borderRadius: '2rem',
                                    color: 'white',
                                    cursor: buildingPickerActive ? 'not-allowed' : 'pointer',
                                    fontSize: '10rem',
                                    fontWeight: buildingPickerActive ? 'bold' : 'normal',
                                    opacity: buildingPickerActive ? 0.6 : 1
                                }}
                                title={buildingPickerActive ? 'Picking mode active - click on buildings in the game' : 'Click to start picking buildings'}
                            >
                                {buildingPickerActive ? '🎯 Picking...' : '+ Building'}
                            </button>
                            <button
                                style={{
                                    padding: '2rem 6rem',
                                    backgroundColor: 'rgba(0,150,255,0.3)',
                                    border: '1px solid rgba(0,150,255,0.5)',
                                    borderRadius: '2rem',
                                    color: 'white',
                                    cursor: 'pointer',
                                    fontSize: '10rem'
                                }}
                                disabled={buildingPickerActive}
                            >
                                + District
                            </button>
                        </div>
                    </div>
                    
                    {/* Buildings list */}
                    {rule.buildings?.length > 0 && rule.buildings.map((building, idx) => (
                        <div key={idx} style={{ 
                            display: 'flex', 
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            padding: '4rem',
                            marginBottom: '4rem',
                            backgroundColor: 'rgba(0,0,0,0.2)',
                            borderRadius: '2rem',
                            fontSize: '11rem'
                        }}>
                            <span>Building {building}</span>
                            <button
                                onClick={() => {
                                    const updatedBuildings = rule.buildings.filter((b) => b !== building);
                                    onUpdate({ ...rule, buildings: updatedBuildings });
                                }}
                                style={{
                                    padding: '2rem 4rem',
                                    backgroundColor: 'transparent',
                                    border: 'none',
                                    cursor: 'pointer',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center'
                                }}
                                title="Remove building"
                            >
                                <img 
                                    src="coui://uil/Colored/XClose.svg"
                                    style={{
                                        width: '16rem',
                                        height: '16rem'
                                    }}
                                    alt="Remove"
                                />
                            </button>
                        </div>
                    ))}
                    
                    {/* Districts list */}
                    {rule.districts?.length > 0 && rule.districts.map((district, idx) => (
                        <div key={idx} style={{ 
                            display: 'flex', 
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            padding: '4rem',
                            marginBottom: '4rem',
                            backgroundColor: 'rgba(0,0,0,0.2)',
                            borderRadius: '2rem',
                            fontSize: '11rem'
                        }}>
                            <span>District {district}</span>
                            <button
                                onClick={() => {
                                    const updatedDistricts = rule.districts.filter((d) => d !== district);
                                    onUpdate({ ...rule, districts: updatedDistricts });
                                }}
                                style={{
                                    padding: '2rem 4rem',
                                    backgroundColor: 'transparent',
                                    border: 'none',
                                    cursor: 'pointer',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center'
                                }}
                                title="Remove district"
                            >
                                <img 
                                    src="coui://uil/Colored/XClose.svg"
                                    style={{
                                        width: '16rem',
                                        height: '16rem'
                                    }}
                                    alt="Remove"
                                />
                            </button>
                        </div>
                    ))}
                    
                    {(!rule.buildings || rule.buildings.length === 0) && 
                     (!rule.districts || rule.districts.length === 0) && (
                        <div style={{ 
                            textAlign: 'center', 
                            color: 'rgba(255,255,255,0.4)',
                            fontSize: '10rem',
                            padding: '8rem'
                        }}>
                            No buildings or districts added
                        </div>
                    )}
                </div>
                
                {/* Right column: Transport Priorities */}
                <div style={{ 
                    padding: '8rem',
                    backgroundColor: 'rgba(255,255,255,0.05)',
                    borderRadius: '3rem'
                }}>
                    <div style={{ 
                        display: 'flex', 
                        justifyContent: 'space-between', 
                        alignItems: 'center',
                        marginBottom: '8rem',
                        fontSize: '12rem',
                        fontWeight: 'bold'
                    }}>
                        <span>Transport Priorities</span>
                        <button
                            style={{
                                padding: '2rem 6rem',
                                backgroundColor: 'rgba(0,150,255,0.3)',
                                border: '1px solid rgba(0,150,255,0.5)',
                                borderRadius: '2rem',
                                color: 'white',
                                cursor: 'pointer',
                                fontSize: '10rem'
                            }}
                        >
                            + Priority
                        </button>
                    </div>
                    
                    {/* Transport priorities list */}
                    {rule.transportPriorities?.length > 0 && rule.transportPriorities.map((priority, idx) => (
                        <div key={idx} style={{ 
                            display: 'flex', 
                            justifyContent: 'space-between',
                            alignItems: 'center',
                            padding: '4rem',
                            marginBottom: '4rem',
                            backgroundColor: 'rgba(0,0,0,0.2)',
                            borderRadius: '2rem',
                            fontSize: '11rem'
                        }}>
                            <span>Priority {priority.priority}</span>
                            <button
                                onClick={() => {
                                    const updatedPriorities = rule.transportPriorities.filter((p) => p.id !== priority.id);
                                    onUpdate({ ...rule, transportPriorities: updatedPriorities });
                                }}
                                style={{
                                    padding: '2rem 4rem',
                                    backgroundColor: 'transparent',
                                    border: 'none',
                                    cursor: 'pointer',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center'
                                }}
                                title="Remove transport priority"
                            >
                                <img 
                                    src="coui://uil/Colored/XClose.svg"
                                    style={{
                                        width: '16rem',
                                        height: '16rem'
                                    }}
                                    alt="Remove"
                                />
                            </button>
                        </div>
                    ))}
                    
                    {(!rule.transportPriorities || rule.transportPriorities.length === 0) && (
                        <div style={{ 
                            textAlign: 'center', 
                            color: 'rgba(255,255,255,0.4)',
                            fontSize: '10rem',
                            padding: '8rem'
                        }}>
                            No transport priorities added
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};

// Management panel component that appears on the right side
// Uses proper game UI module classes like CompanyBrandChanger
const ManageResourceChainsPanel: React.FC<{ 
    entityId: number; 
    onClose: () => void;
    configBinding$?: any;  // Optional: use districtConfig$ for districts
    isDistrict?: boolean;   // Optional: true if this is a district panel
}> = ({ entityId, onClose, configBinding$ = resourceChainConfig$, isDistrict = false }) => {
    const configJson = useValue(configBinding$) as string;
    const buildingPickerActive = useValue(buildingPickerActive$);
    const [config, setConfig] = useState<BuildingConfiguration | null>(null);
    const [isLoading, setIsLoading] = useState<boolean>(true);



    useEffect(() => {
        // Request config when entity changes
        setIsLoading(true);
        const requestType = isDistrict ? "requestDistrictConfig" : "requestBuildingConfig";
        trigger("manageResourceChains", requestType, entityId);
    }, [entityId, isDistrict]);

    useEffect(() => {
        // Parse config when it updates
        try {
            if (configJson && configJson !== "{}") {
                const parsed = JSON.parse(configJson);
                
                // Ensure Rules/rules is an array
                const rulesArray = parsed.Rules || parsed.rules;
                if (!Array.isArray(rulesArray)) {
                    setConfig({
                        buildingEntityId: entityId,
                        rules: []
                    });
                    setIsLoading(false);
                    return;
                }
                
                // Convert camelCase from C# to match our types
                const normalizedConfig: BuildingConfiguration = {
                    buildingEntityId: parsed.BuildingEntityId || parsed.buildingEntityId || entityId,
                    rules: rulesArray.map((r: any) => {
                        // Ensure all arrays exist
                        const buildings = r.Buildings || r.buildings;
                        const districts = r.Districts || r.districts;
                        const transportPriorities = r.TransportPriorities || r.transportPriorities;
                        
                        return {
                            id: r.Id || r.id || Math.random().toString(36).substr(2, 9),
                            color: r.Color || r.color || '#FF0000',
                            type: r.Type ?? r.type ?? ChainType.Incoming,
                            allow: r.Allow ?? r.allow ?? AllowType.Allow,
                            transportType: r.TransportType ?? r.transportType ?? TransportType.Resources,
                            buildings: Array.isArray(buildings) ? buildings : [],
                            districts: Array.isArray(districts) ? districts : [],
                            transportPriorities: Array.isArray(transportPriorities) 
                                ? transportPriorities.map((p: any) => ({
                                    id: p.Id || p.id || Math.random().toString(36).substr(2, 9),
                                    stationType: p.StationType ?? p.stationType ?? TransportStationType.TrainStation,
                                    stationEntity: p.StationEntity ?? p.stationEntity ?? 0,
                                    priority: p.Priority ?? p.priority ?? 1
                                }))
                                : []
                        };
                    })
                };
                
                setConfig(normalizedConfig);
                setIsLoading(false);
            } else {
                setConfig({
                    buildingEntityId: entityId,
                    rules: []
                });
                setIsLoading(false);
            }
        } catch (error) {
            console.error("Error parsing config:", error);
            setConfig({
                buildingEntityId: entityId,
                rules: []
            });
            setIsLoading(false);
        }
    }, [configJson, entityId]);

    const addNewRule = () => {
        const newRule: ResourceChainRule = {
            id: Math.random().toString(36).substr(2, 9),
            color: '#' + Math.floor(Math.random()*16777215).toString(16),
            type: ChainType.Incoming,
            allow: AllowType.Allow,
            transportType: TransportType.Resources,
            buildings: [],
            districts: [],
            transportPriorities: []
        };

        const currentConfig = config || {
            buildingEntityId: entityId,
            rules: []
        };
        
        // Ensure rules is an array
        const currentRules = Array.isArray(currentConfig.rules) ? currentConfig.rules : [];

        const newConfig: BuildingConfiguration = {
            buildingEntityId: currentConfig.buildingEntityId,
            rules: [...currentRules, newRule]
        };
        
        setConfig(newConfig);
    };

    const updateRule = (ruleId: string, updatedRule: ResourceChainRule) => {
        if (!config || !Array.isArray(config.rules)) {
            return;
        }
        
        const newConfig = {
            ...config,
            rules: config.rules.map(r => r.id === ruleId ? updatedRule : r)
        };
        
        setConfig(newConfig);
    };

    const deleteRule = (ruleId: string) => {
        if (!config || !Array.isArray(config.rules)) return;
        
        const newConfig = {
            ...config,
            rules: config.rules.filter(r => r.id !== ruleId)
        };
        setConfig(newConfig);
    };

    const saveAllConfig = () => {
        if (!config) return;
        
        try {
            // First, save current config to memory (not disk)
            const json = JSON.stringify(config);
            const saveType = isDistrict ? "saveDistrictConfig" : "saveBuildingConfig";
            trigger("manageResourceChains", saveType, entityId, json);
            
            // Then trigger disk save for all configurations
            setTimeout(() => {
                trigger("manageResourceChains", "saveAllConfigurations");
                
                // Close the panel after saving
                setTimeout(() => {
                    onClose();
                }, 100); // Small delay to ensure save is triggered
            }, 50); // Small delay to ensure config is updated in memory first
        } catch (error) {
            console.error("Error saving config:", error);
        }
    };


    return (
        <>
            <Panel
                header={(
                    <div style={{ display: 'flex', alignItems: 'center', padding: '0 10rem' }}>
                        <img
                            style={{ width: '24rem', height: '24rem', marginRight: '8rem' }}
                            src="coui://uil/Colored/DeliveryVan.svg"
                            alt="Manage Resource Chains"
                        />
                        <span>Manage Resource Chains</span>
                    </div>
                )}
                onClose={onClose}
                className="manage-resource-chains-panel"
                style={{
                    position: 'absolute',
                    top: 'calc(16rem + var(--floatingToggleSize))',
                    right: '20rem',
                    width: '450rem',
                    maxHeight: 'calc(100vh - 100rem)',
                    opacity: buildingPickerActive ? 0.7 : 1,
                    backgroundColor: buildingPickerActive ? 'rgba(0,0,0,0.6)' : undefined
                }}
                onClick={(e) => {
                    // Handle clicks on the panel to finish picking mode
                    if (buildingPickerActive) {
                        trigger("manageResourceChains", "confirmBuildingPicker");
                        e.stopPropagation(); // Prevent event from bubbling
                    }
                }}
            >
                {/* Overlay message when picking buildings - inside Panel but above content */}
                {buildingPickerActive && (
                    <div style={{
                        position: 'absolute',
                        top: '0',
                        left: '0',
                        right: '0',
                        padding: '20rem 10rem',
                        backgroundColor: 'rgba(255, 165, 0, 0.95)',
                        color: 'white',
                        textAlign: 'center',
                        fontSize: '18rem',
                        fontWeight: 'bold',
                        zIndex: 10000,
                        borderRadius: '8rem 8rem 0 0',
                        boxShadow: '0 4px 12px rgba(0,0,0,0.5)',
                        pointerEvents: 'none',
                        cursor: 'pointer'
                    }}>
                        Click anywhere on the panel to finish
                    </div>
                )}
                
                <div style={{ 
                    paddingTop: buildingPickerActive ? '60rem' : '0', // Add padding when overlay is visible
                    transition: 'padding-top 0.2s ease'
                }}>
                    <Scrollable>
                    {isLoading ? (
                        <div style={{ padding: '20rem', textAlign: 'center', color: 'rgba(255,255,255,0.7)' }}>
                            Loading configuration...
                        </div>
                    ) : (
                        <div>
                            {/* Basic info section */}
                            <div style={{ padding: '10rem', borderBottom: '1px solid rgba(255,255,255,0.1)' }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '5rem' }}>
                                    <span style={{ color: 'rgba(255,255,255,0.7)' }}>Entity ID:</span>
                                    <span>{entityId.toString()}</span>
                                </div>
                                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                                    <span style={{ color: 'rgba(255,255,255,0.7)' }}>Status:</span>
                                    <span>Active</span>
                                </div>
                            </div>
                            
                            {/* Rules section */}
                            <div style={{ padding: '10rem' }}>
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10rem' }}>
                                    <strong>Resource Chain Rules</strong>
                                    <button 
                                        onClick={addNewRule}
                                        style={{ 
                                            padding: '5rem 15rem',
                                            backgroundColor: 'rgba(0, 150, 255, 0.5)',
                                            border: '1px solid rgba(0, 150, 255, 0.8)',
                                            borderRadius: '4rem',
                                            color: 'white',
                                            cursor: 'pointer',
                                            fontSize: '14rem',
                                            fontWeight: 'bold'
                                        }}
                                    >
                                        + Add New Rule
                                    </button>
                                </div>
                                
                                {config && Array.isArray(config.rules) && config.rules.length === 0 && (
                                    <div style={{ padding: '20rem', textAlign: 'center', color: 'rgba(255,255,255,0.5)' }}>
                                        No rules configured. Click "Add New Rule" to create one.
                                    </div>
                                )}
                                
                                {config && Array.isArray(config.rules) && config.rules.length > 0 && (
                                    <div key={`rules-container-${config.rules.length}`}>
                                        {config.rules.map((rule, index) => {
                                            const isValid = rule && 
                                                rule.id &&
                                                rule.color &&
                                                typeof rule.type === 'number' &&
                                                typeof rule.allow === 'number' &&
                                                typeof rule.transportType === 'number' &&
                                                Array.isArray(rule.buildings) &&
                                                Array.isArray(rule.districts) &&
                                                Array.isArray(rule.transportPriorities);
                                            
                                            if (!isValid) {
                                                console.error("Invalid rule detected, skipping render:", rule);
                                                return null;
                                            }
                                            
                                            return (
                                                <ResourceChainRuleComponent
                                                    key={`${rule.id}-${index}`}
                                                    rule={rule}
                                                    entityId={entityId}
                                                    fullConfig={config}
                                                    onUpdate={(updatedRule) => updateRule(rule.id, updatedRule)}
                                                    onDelete={() => deleteRule(rule.id)}
                                                    isDistrict={isDistrict}
                                                />
                                            );
                                        })}
                                    </div>
                                )}
                                
                                {/* Save All Changes button - always visible */}
                                <div style={{ padding: '10rem', display: 'flex', justifyContent: 'center', marginTop: '10rem' }}>
                                    <button 
                                        onClick={saveAllConfig}
                                        style={{ 
                                            padding: '8rem 40rem',
                                            backgroundColor: 'rgba(0, 200, 0, 0.6)',
                                            border: '2px solid rgba(0, 255, 0, 0.8)',
                                            borderRadius: '4rem',
                                            color: 'white',
                                            cursor: 'pointer',
                                            fontSize: '16rem',
                                            fontWeight: 'bold',
                                            boxShadow: '0 2px 8px rgba(0,0,0,0.3)'
                                        }}
                                    >
                                        Save All Changes
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}
                    </Scrollable>
                </div>
            </Panel>
        </>
    );
};

// The actual button component using React/JSX (like FirstPersonCamera)
const ManageResourceChainsButton: React.FC<{ onOpenPanel: () => void }> = ({ onOpenPanel }) => {
    return (
        <DescriptionTooltip title="Manage Resource Chains" description="Configure worker, service, and resource transport rules for this building">
            <button
                style={{ marginLeft: '6rem', marginRight: '8rem' }}
                className="button_Z9O button_ECf item_It6 item-mouse-states_Fmi item-selected_tAM item-focused_FuT button_xGY"
                onClick={onOpenPanel}
            >
                <img 
                    className="icon_Tdt icon_soN icon_Iwk" 
                    src="coui://uil/Colored/DeliveryVan.svg"
                    alt="Manage Resource Chains"
                />
            </button>
        </DescriptionTooltip>
    );
};

// Main component that handles injection
export const BuildingButton = () => {
    const isBuildingSelected = useValue(isBuildingSelected$);
    const selectedBuildingEntity = useValue(selectedBuildingEntity$);
    const [isPanelOpen, setIsPanelOpen] = useState(false);

    // Handle panel open/close
    const handleOpenPanel = () => {
        setIsPanelOpen(true);
    };

    const handleClosePanel = () => {
        setIsPanelOpen(false);
    };

    // Close panel when building is deselected
    useEffect(() => {
        if (!isBuildingSelected || selectedBuildingEntity === 0) {
            setIsPanelOpen(false);
        }
    }, [isBuildingSelected, selectedBuildingEntity]);

    // Handle button injection
    useEffect(() => {
        if (!isBuildingSelected || selectedBuildingEntity === 0) {
            // Remove button when no building is selected
            const container = document.getElementById(BUTTON_CONTAINER_ID);
            if (container) {
                ReactDOM.unmountComponentAtNode(container);
                container.remove();
            }
            return;
        }

        let intervalId: number | undefined;
        let attempts = 0;
        const MAX_ATTEMPTS = 50; // 5 seconds at 100ms intervals
        
        const injectButton = (): boolean => {
            attempts++;
            
            // Find the actions section
            const actionsSection = document.querySelector(ACTIONS_SECTION_CLASS);
            if (!actionsSection) {
                return false; // Keep polling
            }
            
            // Check if button container already exists
            let container = actionsSection.querySelector<HTMLDivElement>(`#${BUTTON_CONTAINER_ID}`);
            if (container) {
                return true; // Success - stop polling
            }
            
            // Create container div for React to render into
            container = document.createElement('div');
            container.id = BUTTON_CONTAINER_ID;
            
            // Insert before the first non-button element (spacer/divider)
            // This ensures we're always in the left group of buttons
            let insertBeforeElement = null;
            for (let i = 0; i < actionsSection.children.length; i++) {
                if (actionsSection.children[i].tagName !== 'BUTTON') {
                    insertBeforeElement = actionsSection.children[i];
                    break;
                }
            }
            
            if (insertBeforeElement) {
                actionsSection.insertBefore(container, insertBeforeElement);
            } else {
                actionsSection.appendChild(container);
            }
            
            // Render the React component into the container (like FirstPersonCamera)
            ReactDOM.render(
                <ManageResourceChainsButton 
                    onOpenPanel={handleOpenPanel}
                />,
                container
            );
            
            return true; // Success - stop polling
        };
        
        // Try immediately
        if (!injectButton()) {
            // Set up polling if first attempt failed
            intervalId = window.setInterval(() => {
                if (injectButton() || attempts >= MAX_ATTEMPTS) {
                    if (intervalId !== undefined) {
                        clearInterval(intervalId);
                        intervalId = undefined;
                    }
                }
            }, 100);
        }
        
        // Cleanup function
        return () => {
            if (intervalId !== undefined) {
                clearInterval(intervalId);
            }
            const container = document.getElementById(BUTTON_CONTAINER_ID);
            if (container) {
                ReactDOM.unmountComponentAtNode(container);
                container.remove();
            }
        };
    }, [isBuildingSelected, selectedBuildingEntity]);

    // Render the panel directly when open (like CompanyBrandChanger does)
    return (
        <>
            {isPanelOpen && selectedBuildingEntity !== 0 && (
                <ManageResourceChainsPanel 
                    entityId={selectedBuildingEntity} 
                    onClose={handleClosePanel}
                />
            )}
        </>
    );
};

// District button component - shows in district panel
export const DistrictButton = () => {
    const isDistrictSelected = useValue(isDistrictSelected$);
    const selectedDistrictEntity = useValue(selectedDistrictEntity$);
    const [isPanelOpen, setIsPanelOpen] = useState(false);

    console.log("🏘️ DistrictButton render - isDistrictSelected:", isDistrictSelected, "selectedDistrictEntity:", selectedDistrictEntity);

    // Handle panel open/close
    const handleOpenPanel = () => {
        console.log("🏘️ District panel opening for entity:", selectedDistrictEntity);
        setIsPanelOpen(true);
    };

    const handleClosePanel = () => {
        console.log("🏘️ District panel closing");
        setIsPanelOpen(false);
    };

    // Close panel when district is deselected
    useEffect(() => {
        if (!isDistrictSelected || selectedDistrictEntity === 0) {
            console.log("🏘️ District deselected, closing panel");
            setIsPanelOpen(false);
        }
    }, [isDistrictSelected, selectedDistrictEntity]);

    // Handle button injection
    useEffect(() => {
        if (!isDistrictSelected || selectedDistrictEntity === 0) {
            // Remove button when no district is selected
            console.log("🏘️ Removing district button - not selected");
            const container = document.getElementById(DISTRICT_BUTTON_CONTAINER_ID);
            if (container) {
                ReactDOM.unmountComponentAtNode(container);
                container.remove();
            }
            return;
        }

        console.log("🏘️ District selected, injecting button for entity:", selectedDistrictEntity);

        let intervalId: number | undefined;
        let attempts = 0;
        const MAX_ATTEMPTS = 50; // 5 seconds at 100ms intervals
        
        const injectButton = (): boolean => {
            attempts++;
            
            // Find the actions section
            const actionsSection = document.querySelector(ACTIONS_SECTION_CLASS);
            if (!actionsSection) {
                if (attempts % 10 === 0) {
                    console.log(`🏘️ Actions section not found (attempt ${attempts}/${MAX_ATTEMPTS})`);
                }
                return false; // Keep polling
            }
            
            // Check if button container already exists
            let container = actionsSection.querySelector<HTMLDivElement>(`#${DISTRICT_BUTTON_CONTAINER_ID}`);
            if (container) {
                console.log("🏘️ District button container already exists");
                return true; // Success - stop polling
            }
            
            // Create container div for React to render into
            container = document.createElement('div');
            container.id = DISTRICT_BUTTON_CONTAINER_ID;
            
            // Insert before the first non-button element (spacer/divider)
            let insertBeforeElement = null;
            for (let i = 0; i < actionsSection.children.length; i++) {
                if (actionsSection.children[i].tagName !== 'BUTTON') {
                    insertBeforeElement = actionsSection.children[i];
                    break;
                }
            }
            
            if (insertBeforeElement) {
                actionsSection.insertBefore(container, insertBeforeElement);
            } else {
                actionsSection.appendChild(container);
            }
            
            console.log("🏘️ District button container created and inserted");
            
            // Render the React component into the container
            ReactDOM.render(
                <ManageResourceChainsButton 
                    onOpenPanel={handleOpenPanel}
                />,
                container
            );
            
            console.log("🏘️ District button rendered successfully");
            return true; // Success - stop polling
        };
        
        // Try immediately
        if (!injectButton()) {
            // Set up polling if first attempt failed
            intervalId = window.setInterval(() => {
                if (injectButton() || attempts >= MAX_ATTEMPTS) {
                    if (intervalId !== undefined) {
                        clearInterval(intervalId);
                        intervalId = undefined;
                    }
                    if (attempts >= MAX_ATTEMPTS) {
                        console.log("🏘️ Failed to inject district button after max attempts");
                    }
                }
            }, 100);
        }
        
        // Cleanup function
        return () => {
            if (intervalId !== undefined) {
                clearInterval(intervalId);
            }
            const container = document.getElementById(DISTRICT_BUTTON_CONTAINER_ID);
            if (container) {
                ReactDOM.unmountComponentAtNode(container);
                container.remove();
            }
        };
    }, [isDistrictSelected, selectedDistrictEntity]);

    // Render the panel directly when open (using districtConfig$ instead of resourceChainConfig$)
    return (
        <>
            {isPanelOpen && selectedDistrictEntity !== 0 && (
                <ManageResourceChainsPanel 
                    entityId={selectedDistrictEntity} 
                    onClose={handleClosePanel}
                    configBinding$={districtConfig$}
                    isDistrict={true}
                />
            )}
        </>
    );
};
