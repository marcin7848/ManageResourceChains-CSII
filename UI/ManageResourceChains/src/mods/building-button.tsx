import React from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { bindValue } from "cs2/api";
import { useEffect } from "react";

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);

const BUTTON_CONTAINER_ID = 'manage-resource-chains-container';
const ACTIONS_SECTION_CLASS = '.actions-section_X1x';

// The actual button component using React/JSX (like FirstPersonCamera)
const ManageResourceChainsButton: React.FC<{ entityId: number }> = ({ entityId }) => {
    const handleClick = () => {
        console.log("Manage Resource Chains clicked for entity:", entityId);
        // TODO: Open management panel
        // trigger("manageResourceChains", "OpenPanel", entityId);
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
                <ManageResourceChainsButton entityId={selectedBuildingEntity} />,
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

