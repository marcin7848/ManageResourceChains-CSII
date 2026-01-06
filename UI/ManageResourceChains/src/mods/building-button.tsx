import React, { useState, useEffect } from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { bindValue, trigger } from "cs2/api";
import { getModule } from "cs2/modding";
import { Portal, Dropdown, DropdownToggle, Panel, Scrollable } from "cs2/ui";
import { Color } from "cs2/bindings";

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
    TransportStationType,
    TransportPriority
} from "./types";

// Import game UI styles like CompanyBrandChanger does
const stylePanel = getModule("game-ui/common/panel/panel.module.scss", "classes");
const styleDefault = getModule("game-ui/common/panel/themes/default.module.scss", "classes");
const styleIcon = getModule("game-ui/common/input/button/icon-button.module.scss", "classes");
const styleTintedIcon = getModule("game-ui/common/image/tinted-icon.module.scss", "classes");
const styleCloseButton = getModule("game-ui/common/input/button/themes/round-highlight-button.module.scss", "classes");
const styleDropdown = getModule("game-ui/menu/themes/dropdown.module.scss", "classes");

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);
const resourceChainConfig$ = bindValue<string>("manageResourceChains", "resourceChainConfig", "{}");
const buildingPickerActive$ = bindValue<boolean>("manageResourceChains", "buildingPickerActive", false);

const BUTTON_CONTAINER_ID = 'manage-resource-chains-container';
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
    onUpdate: (rule: ResourceChainRule) => void;
    onDelete: () => void;
}> = ({ rule, entityId, onUpdate, onDelete }) => {
    
    console.log("ResourceChainRuleComponent rendering, rule.id:", rule?.id);
    
    const buildingPickerActive = useValue(buildingPickerActive$);
    const [isPickingBuildings, setIsPickingBuildings] = useState(false);
    
    // Sync local state with global binding
    useEffect(() => {
        if (!buildingPickerActive && isPickingBuildings) {
            setIsPickingBuildings(false);
        }
    }, [buildingPickerActive, isPickingBuildings]);
    
    const startBuildingPicker = () => {
        console.log("🎯 Starting building picker for rule:", rule.id);
        console.log("🎯 Current isPickingBuildings:", isPickingBuildings);
        console.log("🎯 Current buildingPickerActive:", buildingPickerActive);
        console.log("🎯 Set isPickingBuildings to true");
        setIsPickingBuildings(true);
        console.log("🎯 Triggering C# startBuildingPicker with entityId:", entityId, "ruleId:", rule.id);
        trigger("manageResourceChains", "startBuildingPicker", entityId, rule.id);
        console.log("🎯 C# trigger sent");
    };

    const confirmBuildingPicker = () => {
        console.log("✅ Confirming building picker");
        setIsPickingBuildings(false);
        trigger("manageResourceChains", "confirmBuildingPicker");
    };

    const cancelBuildingPicker = () => {
        console.log("❌ Cancelling building picker");
        setIsPickingBuildings(false);
        trigger("manageResourceChains", "cancelBuildingPicker");
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
                                style={{
                                    padding: '2rem 6rem',
                                    backgroundColor: isPickingBuildings ? 'rgba(255,165,0,0.5)' : 'rgba(0,150,255,0.3)',
                                    border: `1px solid ${isPickingBuildings ? 'rgba(255,165,0,0.8)' : 'rgba(0,150,255,0.5)'}`,
                                    borderRadius: '2rem',
                                    color: 'white',
                                    cursor: 'pointer',
                                    fontSize: '10rem'
                                }}
                                title={isPickingBuildings ? 'Click buildings, then click Done' : 'Add buildings to this rule'}
                                disabled={isPickingBuildings}
                            >
                                {isPickingBuildings ? '🎯 Picking...' : '+ Building'}
                            </button>
                            {isPickingBuildings && (
                                <>
                                    <button
                                        onClick={confirmBuildingPicker}
                                        style={{
                                            padding: '2rem 6rem',
                                            backgroundColor: 'rgba(0,255,0,0.3)',
                                            border: '1px solid rgba(0,255,0,0.5)',
                                            borderRadius: '2rem',
                                            color: 'white',
                                            cursor: 'pointer',
                                            fontSize: '10rem',
                                            fontWeight: 'bold'
                                        }}
                                        title="Confirm selection and restore panel"
                                    >
                                        ✅ Done
                                    </button>
                                    <button
                                        onClick={cancelBuildingPicker}
                                        style={{
                                            padding: '2rem 6rem',
                                            backgroundColor: 'rgba(255,0,0,0.3)',
                                            border: '1px solid rgba(255,0,0,0.5)',
                                            borderRadius: '2rem',
                                            color: 'white',
                                            cursor: 'pointer',
                                            fontSize: '10rem'
                                        }}
                                        title="Cancel selection and restore panel"
                                    >
                                        ❌ Cancel
                                    </button>
                                </>
                            )}
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
                                disabled={isPickingBuildings}
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
                                    console.log(`Removing building ${building} from rule ${rule.id}`);
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
                                    console.log(`Removing district ${district} from rule ${rule.id}`);
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
                                    console.log(`Removing transport priority ${priority.id} from rule ${rule.id}`);
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
const ManageResourceChainsPanel: React.FC<{ entityId: number; onClose: () => void }> = ({ entityId, onClose }) => {
    const configJson = useValue(resourceChainConfig$);
    const buildingPickerActive = useValue(buildingPickerActive$);
    const [config, setConfig] = useState<BuildingConfiguration | null>(null);
    const [isLoading, setIsLoading] = useState<boolean>(true);
    
    // Debug: Log when buildingPickerActive changes
    useEffect(() => {
        console.log("🔍 Panel: buildingPickerActive changed to:", buildingPickerActive);
    }, [buildingPickerActive]);



    useEffect(() => {
        // Request config when entity changes
        setIsLoading(true);
        trigger("manageResourceChains", "requestBuildingConfig", entityId);
    }, [entityId]);

    useEffect(() => {
        // Parse config when it updates
        try {
            console.log("Received configJson:", configJson);
            
            if (configJson && configJson !== "{}") {
                const parsed = JSON.parse(configJson);
                console.log("Parsed config:", JSON.stringify(parsed, null, 2));
                
                // Ensure Rules/rules is an array
                const rulesArray = parsed.Rules || parsed.rules;
                if (!Array.isArray(rulesArray)) {
                    console.error("Rules is not an array:", rulesArray);
                    setConfig({
                        buildingEntityId: entityId,
                        rules: []
                    });
                    setIsLoading(false);
                    return;
                }
                
                console.log("Rules array has", rulesArray.length, "rules");
                
                // Convert camelCase from C# to match our types
                const normalizedConfig: BuildingConfiguration = {
                    buildingEntityId: parsed.BuildingEntityId || parsed.buildingEntityId || entityId,
                    rules: rulesArray.map((r: any, index: number) => {
                        console.log(`Normalizing rule ${index}:`, JSON.stringify(r, null, 2));
                        
                        // Ensure all arrays exist
                        const buildings = r.Buildings || r.buildings;
                        const districts = r.Districts || r.districts;
                        const transportPriorities = r.TransportPriorities || r.transportPriorities;
                        
                        const normalizedRule = {
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
                        
                        console.log(`Normalized rule ${index} result:`, JSON.stringify(normalizedRule, null, 2));
                        return normalizedRule;
                    })
                };
                
                console.log("Final normalized config:", JSON.stringify(normalizedConfig, null, 2));
                console.log("Setting config with", normalizedConfig.rules.length, "rules");
                setConfig(normalizedConfig);
                setIsLoading(false);
                console.log("Config set and loading complete");
            } else {
                console.log("Empty config, initializing with defaults");
                setConfig({
                    buildingEntityId: entityId,
                    rules: []
                });
                setIsLoading(false);
            }
        } catch (error) {
            console.error("Error parsing config:", error, "configJson:", configJson);
            setConfig({
                buildingEntityId: entityId,
                rules: []
            });
            setIsLoading(false);
        }
    }, [configJson, entityId]);
    const [panelStyle, setPanelStyle] = useState<React.CSSProperties>({
        position: 'absolute',
        top: 'calc(16rem + var(--floatingToggleSize))',
        bottom: '6rem',
        right: '20rem',
        width: '450rem',
        zIndex: '9999' // High z-index to ensure dropdowns appear on top
    });

    const addNewRule = () => {
        console.log("addNewRule called, current config:", JSON.stringify(config, null, 2));
        
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

        console.log("Created new rule:", JSON.stringify(newRule, null, 2));

        const currentConfig = config || {
            buildingEntityId: entityId,
            rules: []
        };

        console.log("Current config for merge:", JSON.stringify(currentConfig, null, 2));
        
        // Ensure rules is an array
        const currentRules = Array.isArray(currentConfig.rules) ? currentConfig.rules : [];
        console.log("Current rules array:", JSON.stringify(currentRules, null, 2), "length:", currentRules.length);

        const newConfig: BuildingConfiguration = {
            buildingEntityId: currentConfig.buildingEntityId,
            rules: [...currentRules, newRule]
        };
        
        console.log("New config to set:", JSON.stringify(newConfig, null, 2));
        console.log("Verifying new config rules is array:", Array.isArray(newConfig.rules), "length:", newConfig.rules.length);
        
        // Double check the rule we're about to render
        console.log("Rule that will be rendered:", JSON.stringify(newConfig.rules[newConfig.rules.length - 1], null, 2));
        
        setConfig(newConfig);
        console.log("Config set successfully");
    };

    const updateRule = (ruleId: string, updatedRule: ResourceChainRule) => {
        console.log("updateRule called for:", ruleId, "with:", updatedRule);
        
        if (!config || !Array.isArray(config.rules)) {
            console.error("updateRule: config or rules invalid", config);
            return;
        }
        
        const newConfig = {
            ...config,
            rules: config.rules.map(r => r.id === ruleId ? updatedRule : r)
        };
        
        console.log("updateRule: new config:", newConfig);
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
            const json = JSON.stringify(config);
            trigger("manageResourceChains", "saveBuildingConfig", entityId, json);
            console.log("Configuration saved successfully");
        } catch (error) {
            console.error("Error saving config:", error);
        }
    };

    useEffect(() => {
        const calculatePosition = () => {
            // Find the building info panel (selected-info-panel)
            const sipElement = document.querySelector('.selected-info-panel_gG8') as HTMLElement | null;
            const wrapperElement = document.querySelector('.info-layout_BVk') as HTMLElement | null;
            
            if (sipElement && sipElement.offsetWidth > 0) {
                const newPanelLeft = sipElement.offsetLeft + sipElement.offsetWidth;
                const maxHeight = wrapperElement?.offsetHeight ?? 1600;
                
                setPanelStyle({
                    position: 'absolute',
                    left: `calc(${newPanelLeft}px + 20rem)`,
                    top: 'calc(16rem + var(--floatingToggleSize))',
                    bottom: '6rem',
                    width: '400rem',
                    maxHeight: `${maxHeight}px`,
                    zIndex: '9999', // High z-index to ensure dropdowns appear on top
                    overflow: 'visible' // Changed from hidden to visible for dropdowns
                });
            }
        };

        // Calculate immediately
        calculatePosition();

        // Observe DOM changes to recalculate position
        const observer = new MutationObserver(() => {
            calculatePosition();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        return () => observer.disconnect();
    }, [entityId]);


    return (
        <>
            {/* Don't render panel when building picker is active - this allows raycasting to reach buildings */}
            {!buildingPickerActive && (
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
                        maxHeight: 'calc(100vh - 100rem)'
                    }}
                >
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
                                                typeof rule.id === 'string' &&
                                                typeof rule.color === 'string' &&
                                                typeof rule.type === 'number' &&
                                                typeof rule.allow === 'number' &&
                                                typeof rule.transportType === 'number' &&
                                                Array.isArray(rule.buildings) &&
                                                Array.isArray(rule.districts) &&
                                                Array.isArray(rule.transportPriorities);
                                            
                                            if (!isValid) {
                                                console.error("Invalid rule detected, skipping render:", JSON.stringify(rule, null, 2));
                                                return null;
                                            }
                                            
                                            return (
                                                <ResourceChainRuleComponent
                                                    key={`${rule.id}-${index}`}
                                                    rule={rule}
                                                    entityId={entityId}
                                                    onUpdate={(updatedRule) => updateRule(rule.id, updatedRule)}
                                                    onDelete={() => deleteRule(rule.id)}
                                                />
                                            );
                                        })}
                                    </div>
                                )}
                                
                                {config && Array.isArray(config.rules) && config.rules.length > 0 && (
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
                                )}
                            </div>
                        </div>
                    )}
                </Scrollable>
            </Panel>
            )}
            
            {/* Floating control panel for building picker - rendered in Portal */}
            {buildingPickerActive && (
                <Portal>
                    {/* Control panel - positioned at top center, pointer-events only on the panel itself */}
                    <div 
                        style={{
                            position: 'fixed',
                            top: '100rem',
                            left: '50%',
                            transform: 'translateX(-50%)',
                            zIndex: 999999,
                            backgroundColor: 'rgba(0, 0, 0, 0.95)',
                            border: '3px solid rgba(255, 165, 0, 0.9)',
                            borderRadius: '8rem',
                            padding: '20rem 30rem',
                            boxShadow: '0 8px 32px rgba(0, 0, 0, 0.8)',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '15rem',
                            alignItems: 'center',
                            pointerEvents: 'auto',
                            userSelect: 'none'
                        }}
                        onMouseDown={(e) => e.stopPropagation()}
                        onMouseUp={(e) => e.stopPropagation()}
                        onClick={(e) => e.stopPropagation()}
                    >
                            <div style={{
                                fontSize: '18rem',
                                fontWeight: 'bold',
                                color: '#FFA500',
                                marginBottom: '5rem'
                            }}>
                                🎯 Building Picker Active
                            </div>
                            <div style={{
                                fontSize: '14rem',
                                color: 'rgba(255, 255, 255, 0.9)',
                                textAlign: 'center',
                                marginBottom: '10rem'
                            }}>
                                Click on buildings to select them<br />
                                Then click Done or Cancel below
                            </div>
                            <div style={{
                                display: 'flex',
                                gap: '15rem'
                            }}>
                                <button
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        e.preventDefault();
                                        console.log("✅ Done button clicked");
                                        trigger("manageResourceChains", "confirmBuildingPicker");
                                    }}
                                    onMouseDown={(e) => {
                                        e.stopPropagation();
                                        e.preventDefault();
                                    }}
                                    onMouseUp={(e) => {
                                        e.stopPropagation();
                                        e.preventDefault();
                                    }}
                                    style={{
                                        padding: '12rem 30rem',
                                        backgroundColor: 'rgba(0, 255, 0, 0.4)',
                                        border: '3px solid rgba(0, 255, 0, 0.8)',
                                        borderRadius: '6rem',
                                        color: 'white',
                                        cursor: 'pointer',
                                        fontSize: '16rem',
                                        fontWeight: 'bold',
                                        pointerEvents: 'auto',
                                        userSelect: 'none'
                                    }}
                                >
                                    ✅ Done
                                </button>
                                <button
                                    onClick={(e) => {
                                        e.stopPropagation();
                                        e.preventDefault();
                                        console.log("❌ Cancel button clicked");
                                        trigger("manageResourceChains", "cancelBuildingPicker");
                                    }}
                                    onMouseDown={(e) => {
                                        e.stopPropagation();
                                        e.preventDefault();
                                    }}
                                    onMouseUp={(e) => {
                                        e.stopPropagation();
                                        e.preventDefault();
                                    }}
                                    style={{
                                        padding: '12rem 30rem',
                                        backgroundColor: 'rgba(255, 0, 0, 0.4)',
                                        border: '3px solid rgba(255, 0, 0, 0.8)',
                                        borderRadius: '6rem',
                                        color: 'white',
                                        cursor: 'pointer',
                                        fontSize: '16rem',
                                        pointerEvents: 'auto',
                                        userSelect: 'none'
                                    }}
                                >
                                    ❌ Cancel
                                </button>
                            </div>
                        </div>
                    </Portal>
            )}
        </>
    );
};

// The actual button component using React/JSX (like FirstPersonCamera)
const ManageResourceChainsButton: React.FC<{ onOpenPanel: () => void }> = ({ onOpenPanel }) => {
    return (
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

