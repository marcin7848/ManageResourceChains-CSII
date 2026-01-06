import React, { useState, useEffect } from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { bindValue, trigger } from "cs2/api";
import { getModule } from "cs2/modding";
import { Portal } from "cs2/ui";
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

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);
const resourceChainConfig$ = bindValue<string>("manageResourceChains", "resourceChainConfig", "{}");

const BUTTON_CONTAINER_ID = 'manage-resource-chains-container';
const ACTIONS_SECTION_CLASS = '.actions-section_X1x';

// Completely static component - NO useEffect, parent handles editing
const ResourceChainRuleComponent: React.FC<{
    rule: ResourceChainRule;
    entityId: number;
    onUpdate: (rule: ResourceChainRule) => void;
    onDelete: () => void;
    onEditClick: (ruleId: string) => void;
}> = ({ rule, entityId, onUpdate, onDelete, onEditClick }) => {
    
    console.log("ResourceChainRuleComponent rendering, rule.id:", rule?.id);
    
    // Completely static display
    return (
        <div style={{ 
            padding: '10rem', 
            marginBottom: '10rem', 
            border: '1px solid rgba(255,255,255,0.2)',
            borderRadius: '4rem',
            backgroundColor: 'rgba(0,0,0,0.2)'
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10rem' }}>
                <div style={{ 
                    width: '30rem', 
                    height: '30rem', 
                    backgroundColor: rule?.color || '#FF0000',
                    border: '2px solid rgba(255,255,255,0.5)',
                    borderRadius: '4rem'
                }} />
                <button
                    onClick={() => onEditClick(rule.id)}
                    style={{
                        padding: '4rem 12rem',
                        backgroundColor: 'rgba(0, 150, 255, 0.5)',
                        border: '1px solid rgba(0, 150, 255, 0.8)',
                        borderRadius: '3rem',
                        color: 'white',
                        cursor: 'pointer',
                        fontSize: '12rem'
                    }}
                >
                    ✏️ Edit
                </button>
            </div>
            
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '5rem', fontSize: '12rem' }}>
                <div>
                    <strong>Type:</strong> {rule?.type === ChainType.Incoming ? 'Incoming' : 'Outgoing'}
                </div>
                <div>
                    <strong>Allow:</strong> {rule?.allow === AllowType.Allow ? 'Allow' : 'Disallow'}
                </div>
                <div style={{ gridColumn: '1 / -1' }}>
                    <strong>Transport:</strong> {
                        rule?.transportType === TransportType.Workers ? 'Workers' :
                        rule?.transportType === TransportType.Services ? 'Services' :
                        'Resources'
                    }
                </div>
            </div>
            
            <div style={{ marginTop: '8rem', fontSize: '11rem', color: 'rgba(255,255,255,0.6)' }}>
                Buildings: {Array.isArray(rule?.buildings) ? rule.buildings.length : 0} | 
                Districts: {Array.isArray(rule?.districts) ? rule.districts.length : 0} | 
                Priorities: {Array.isArray(rule?.transportPriorities) ? rule.transportPriorities.length : 0}
            </div>
        </div>
    );
};

// Management panel component that appears on the right side
// Uses proper game UI module classes like CompanyBrandChanger
const ManageResourceChainsPanel: React.FC<{ entityId: number; onClose: () => void }> = ({ entityId, onClose }) => {
    const configJson = useValue(resourceChainConfig$);
    const [config, setConfig] = useState<BuildingConfiguration | null>(null);
    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [editingRuleId, setEditingRuleId] = useState<string | null>(null);

    // ...existing useEffects...

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
        zIndex: 'calc(var(--tooltipIndex) - 1)' as any
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
                    zIndex: 'calc(var(--tooltipIndex) - 1)' as any,
                    overflow: 'hidden'
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
        <Portal>
            <div 
                style={panelStyle}
                className={stylePanel.panel}
            >
                <div className={styleDefault.header}>
                    <div className={stylePanel.titleBar}>
                        <img
                            className={stylePanel.icon}
                            src="coui://uil/Colored/DeliveryVan.svg"
                            alt="Manage Resource Chains"
                        />
                        <div className={styleDefault.title}>Manage Resource Chains</div>
                        <button 
                            className={`${styleCloseButton.button} ${stylePanel.closeButton}`}
                            onClick={onClose}
                        >
                            <div 
                                className={`${styleTintedIcon.tintedIcon} ${styleIcon.icon}`}
                                style={{ 
                                    maskImage: 'url(Media/Glyphs/Close.svg)',
                                    WebkitMaskImage: 'url(Media/Glyphs/Close.svg)'
                                }}
                            />
                        </button>
                    </div>
                </div>
                
                <div className={styleDefault.content}>
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
                                            // Validate rule before rendering
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
                                            
                                            const isEditing = editingRuleId === rule.id;
                                            
                                            return (
                                                <div key={`${rule.id}-${index}`}>
                                                    <ResourceChainRuleComponent
                                                        rule={rule}
                                                        entityId={entityId}
                                                        onUpdate={(updatedRule) => updateRule(rule.id, updatedRule)}
                                                        onDelete={() => deleteRule(rule.id)}
                                                        onEditClick={(ruleId) => setEditingRuleId(isEditing ? null : ruleId)}
                                                    />
                                                    
                                                    {/* Inline editor - only shown when editing */}
                                                    {isEditing && (
                                                        <div style={{ 
                                                            padding: '10rem',
                                                            marginBottom: '10rem',
                                                            backgroundColor: 'rgba(0,150,255,0.1)',
                                                            border: '1px solid rgba(0,150,255,0.3)',
                                                            borderRadius: '4rem'
                                                        }}>
                                                            <div style={{ marginBottom: '8rem' }}>
                                                                <strong>Edit Type:</strong>
                                                                <div style={{ display: 'flex', gap: '5rem', marginTop: '5rem' }}>
                                                                    <button
                                                                        onClick={() => {
                                                                            updateRule(rule.id, { ...rule, type: ChainType.Incoming });
                                                                        }}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.type === ChainType.Incoming ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.type === ChainType.Incoming ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Incoming
                                                                    </button>
                                                                    <button
                                                                        onClick={() => {
                                                                            updateRule(rule.id, { ...rule, type: ChainType.Outgoing });
                                                                        }}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.type === ChainType.Outgoing ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.type === ChainType.Outgoing ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Outgoing
                                                                    </button>
                                                                </div>
                                                            </div>
                                                            
                                                            <div style={{ marginBottom: '8rem' }}>
                                                                <strong>Edit Allow:</strong>
                                                                <div style={{ display: 'flex', gap: '5rem', marginTop: '5rem' }}>
                                                                    <button
                                                                        onClick={() => updateRule(rule.id, { ...rule, allow: AllowType.Allow })}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.allow === AllowType.Allow ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.allow === AllowType.Allow ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Allow
                                                                    </button>
                                                                    <button
                                                                        onClick={() => updateRule(rule.id, { ...rule, allow: AllowType.Disallow })}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.allow === AllowType.Disallow ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.allow === AllowType.Disallow ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Disallow
                                                                    </button>
                                                                </div>
                                                            </div>
                                                            
                                                            <div style={{ marginBottom: '8rem' }}>
                                                                <strong>Edit Transport:</strong>
                                                                <div style={{ display: 'flex', gap: '5rem', marginTop: '5rem', flexWrap: 'wrap' }}>
                                                                    <button
                                                                        onClick={() => updateRule(rule.id, { ...rule, transportType: TransportType.Workers })}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.transportType === TransportType.Workers ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.transportType === TransportType.Workers ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Workers
                                                                    </button>
                                                                    <button
                                                                        onClick={() => updateRule(rule.id, { ...rule, transportType: TransportType.Services })}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.transportType === TransportType.Services ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.transportType === TransportType.Services ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Services
                                                                    </button>
                                                                    <button
                                                                        onClick={() => updateRule(rule.id, { ...rule, transportType: TransportType.Resources })}
                                                                        style={{
                                                                            padding: '5rem 10rem',
                                                                            backgroundColor: rule.transportType === TransportType.Resources ? 'rgba(0,255,0,0.3)' : 'rgba(255,255,255,0.1)',
                                                                            border: `1px solid ${rule.transportType === TransportType.Resources ? 'rgba(0,255,0,0.5)' : 'rgba(255,255,255,0.3)'}`,
                                                                            borderRadius: '3rem',
                                                                            color: 'white',
                                                                            cursor: 'pointer'
                                                                        }}
                                                                    >
                                                                        Resources
                                                                    </button>
                                                                </div>
                                                            </div>
                                                            
                                                            <button
                                                                onClick={() => deleteRule(rule.id)}
                                                                style={{
                                                                    padding: '5rem 10rem',
                                                                    backgroundColor: 'rgba(255,0,0,0.3)',
                                                                    border: '1px solid rgba(255,0,0,0.5)',
                                                                    borderRadius: '3rem',
                                                                    color: '#ff4444',
                                                                    cursor: 'pointer',
                                                                    fontWeight: 'bold'
                                                                }}
                                                            >
                                                                🗑️ Delete Rule
                                                            </button>
                                                        </div>
                                                    )}
                                                </div>
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
                                            💾 Save All Changes
                                        </button>
                                    </div>
                                )}
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </Portal>
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

