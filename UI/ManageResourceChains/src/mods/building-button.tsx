import React, { useState } from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { bindValue } from "cs2/api";
import { useEffect } from "react";

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);

const BUTTON_CONTAINER_ID = 'manage-resource-chains-container';
const PANEL_CONTAINER_ID = 'manage-resource-chains-panel-container';
const ACTIONS_SECTION_CLASS = '.actions-section_X1x';

// Management panel component that appears on the right side
const ManageResourceChainsPanel: React.FC<{ entityId: number; onClose: () => void }> = ({ entityId, onClose }) => {
    return (
        <div 
            style={{
                position: 'fixed',
                top: '100rem',
                right: '20rem',
                width: '400rem',
                backgroundColor: 'rgba(0, 0, 0, 0.85)',
                border: '2rem solid rgba(255, 255, 255, 0.2)',
                borderRadius: '4rem',
                padding: '20rem',
                zIndex: 10000,
                color: 'white',
                fontFamily: 'sans-serif'
            }}
            className="panel_YqS"
        >
            {/* Header with title and close button */}
            <div style={{ 
                display: 'flex', 
                justifyContent: 'space-between', 
                alignItems: 'center',
                marginBottom: '15rem',
                borderBottom: '1rem solid rgba(255, 255, 255, 0.2)',
                paddingBottom: '10rem'
            }}>
                <h2 style={{ 
                    margin: 0, 
                    fontSize: '18rem',
                    fontWeight: 'bold'
                }}>
                    Manage Resource Chains
                </h2>
                <button
                    onClick={onClose}
                    style={{
                        background: 'rgba(255, 255, 255, 0.1)',
                        border: '1rem solid rgba(255, 255, 255, 0.3)',
                        borderRadius: '3rem',
                        color: 'white',
                        fontSize: '16rem',
                        fontWeight: 'bold',
                        width: '30rem',
                        height: '30rem',
                        cursor: 'pointer',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        padding: 0
                    }}
                    className="button_ECf item-mouse-states_Fmi"
                >
                    ×
                </button>
            </div>

            {/* Panel content */}
            <div style={{ padding: '10rem 0' }}>
                <p style={{ margin: '0 0 10rem 0', fontSize: '14rem' }}>
                    Entity ID: {entityId}
                </p>
                <p style={{ margin: 0, fontSize: '14rem', color: 'rgba(255, 255, 255, 0.7)' }}>
                    Resource chain management coming soon...
                </p>
            </div>
        </div>
    );
};

// The actual button component using React/JSX (like FirstPersonCamera)
const ManageResourceChainsButton: React.FC<{ entityId: number; onOpenPanel: () => void }> = ({ entityId, onOpenPanel }) => {
    const handleClick = () => {
        console.log("Manage Resource Chains clicked for entity:", entityId);
        onOpenPanel();
    };

    return (
        <button
            style={{ marginLeft: '6rem', marginRight: '8rem' }}
            className="button_Z9O button_ECf item_It6 item-mouse-states_Fmi item-selected_tAM item-focused_FuT button_xGY"
            onClick={handleClick}
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

    // Render the panel in the document body when open
    useEffect(() => {
        if (isPanelOpen && selectedBuildingEntity !== 0) {
            const panelRoot = document.createElement('div');
            panelRoot.id = PANEL_CONTAINER_ID;
            document.body.appendChild(panelRoot);

            ReactDOM.render(
                <ManageResourceChainsPanel 
                    entityId={selectedBuildingEntity} 
                    onClose={handleClosePanel}
                />,
                panelRoot
            );

            return () => {
                ReactDOM.unmountComponentAtNode(panelRoot);
                if (panelRoot.parentNode) {
                    panelRoot.parentNode.removeChild(panelRoot);
                }
            };
        }
    }, [isPanelOpen, selectedBuildingEntity]);

    // Handle button injection
    useEffect(() => {
        if (!isBuildingSelected || selectedBuildingEntity === 0) {
            // Remove button when no building is selected
            const container = document.getElementById(BUTTON_CONTAINER_ID);
            if (container) {
                ReactDOM.unmountComponentAtNode(container);
                container.remove();
            }
            // Close panel when building is deselected
            setIsPanelOpen(false);
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
                    entityId={selectedBuildingEntity} 
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

    // This component doesn't render anything itself - it injects into the DOM
    return null;
};

