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
// Uses the same panel classes as FirstPersonCamera and other game UI panels
const ManageResourceChainsPanel: React.FC<{ entityId: number; onClose: () => void }> = ({ entityId, onClose }) => {
    return (
        <div 
            style={{ 
                position: 'fixed',
                top: '100rem',
                right: '20rem',
                width: '400rem',
                maxHeight: '80vh'
            }}
            className="panel_YqS expanded"
        >
            <div className="header_H_U header_Bpo header_xQg">
                <div className="title-bar_RFC">
                    <div className="title_SVH title_zQN">Manage Resource Chains</div>
                    <button 
                        className="button_s2g button_ECf close-button_wKK"
                        onClick={onClose}
                    >
                        <div className="tinted-icon_iKo" style={{ 
                            maskImage: 'url(coui://uil/Standard/XClose.svg)',
                            WebkitMaskImage: 'url(coui://uil/Standard/XClose.svg)'
                        }}></div>
                    </button>
                </div>
            </div>
            
            <div className="content_XD5 content_AD7 child-opacity-transition_nkS">
                <div className="scrollable_DXr y_SMM scrollable_wt8">
                    <div className="content_gqa">
                        <div className="infoview-panel-section_RXJ">
                            <div className="labels_L7Q">
                                <div className="label_l_4 label_uCB uppercase_RJI">Entity Information</div>
                            </div>
                            <div className="content_1xS">
                                <div className="row_S2v">
                                    <div className="left_Yja row_S2v">Entity ID:</div>
                                    <div className="right_k3O row_S2v">{entityId}</div>
                                </div>
                                <div className="row_S2v">
                                    <div className="left_Yja row_S2v">Status:</div>
                                    <div className="right_k3O row_S2v">Active</div>
                                </div>
                            </div>
                        </div>
                        
                        <div className="infoview-panel-section_RXJ" style={{ marginTop: '10rem' }}>
                            <div className="labels_L7Q">
                                <div className="label_l_4 label_uCB uppercase_RJI">Resource Chains</div>
                            </div>
                            <div className="content_1xS">
                                <div className="row_S2v">
                                    <div className="left_Yja row_S2v" style={{ color: 'rgba(255, 255, 255, 0.7)' }}>
                                        Resource chain management coming soon...
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
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

