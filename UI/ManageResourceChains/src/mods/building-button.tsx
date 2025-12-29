import React, { useState } from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { bindValue } from "cs2/api";
import { useEffect } from "react";
import { getModule } from "cs2/modding";
import { PanelSection, PanelSectionRow, Portal } from "cs2/ui";

// Import game UI styles like CompanyBrandChanger does
const stylePanel = getModule("game-ui/common/panel/panel.module.scss", "classes");
const styleDefault = getModule("game-ui/common/panel/themes/default.module.scss", "classes");
const styleIcon = getModule("game-ui/common/input/button/icon-button.module.scss", "classes");
const styleTintedIcon = getModule("game-ui/common/image/tinted-icon.module.scss", "classes");
const styleCloseButton = getModule("game-ui/common/input/button/themes/round-highlight-button.module.scss", "classes");

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);

const BUTTON_CONTAINER_ID = 'manage-resource-chains-container';
const ACTIONS_SECTION_CLASS = '.actions-section_X1x';

// Management panel component that appears on the right side
// Uses proper game UI module classes like CompanyBrandChanger
const ManageResourceChainsPanel: React.FC<{ entityId: number; onClose: () => void }> = ({ entityId, onClose }) => {
    const [panelStyle, setPanelStyle] = useState<React.CSSProperties>({
        position: 'absolute',
        top: 'calc(16rem + var(--floatingToggleSize))',
        bottom: '6rem',
        right: '20rem',
        width: '400rem',
        zIndex: 'calc(var(--tooltipIndex) - 1)' as any
    });

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
                    <PanelSection>
                        <PanelSectionRow
                            left="Entity ID:"
                            right={entityId.toString()}
                        />
                        <PanelSectionRow
                            left="Status:"
                            right="Active"
                        />
                    </PanelSection>
                    
                    <PanelSection>
                        <PanelSectionRow
                            left="Resource Chains"
                            right="Coming soon..."
                        />
                    </PanelSection>
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

