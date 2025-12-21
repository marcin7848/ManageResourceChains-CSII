import { useValue } from "cs2/api";
import { bindValue } from "cs2/api";
import { useEffect } from "react";

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);

export const BuildingButton = () => {
    const isBuildingSelected = useValue(isBuildingSelected$);
    const selectedBuildingEntity = useValue(selectedBuildingEntity$);

    console.log("BuildingButton render - isBuildingSelected:", isBuildingSelected, "selectedBuildingEntity:", selectedBuildingEntity);

    useEffect(() => {
        if (isBuildingSelected && selectedBuildingEntity !== 0) {
            console.log("BuildingButton: Starting polling for actions section");
            
            // Clear any existing interval
            if ((window as any).manageResourceChainsInterval) {
                clearInterval((window as any).manageResourceChainsInterval);
            }
            
            // Use polling like FirstPersonCamera does
            const checkAndInject = () => {
                // Target the actions section where FOCUS and TOGGLE TRAFFIC ROUTES buttons are
                const actionsSection = document.querySelector('.actions-section_X1x');
                
                if (!actionsSection) {
                    console.log("Actions section not found yet, will keep polling...");
                    return;
                }
                
                // Check if button already exists
                const existingButton = actionsSection.querySelector('#manage-resource-chains-btn');
                if (existingButton) {
                    console.log("Button already exists");
                    return;
                }
                
                console.log("Found actions section! Injecting button...");
                
                const buttonContainer = document.createElement('div');
                buttonContainer.id = 'manage-resource-chains-btn';
                buttonContainer.style.cssText = 'margin-left: 6rem; margin-right: 8rem; display: inline-block;';
                
                const button = document.createElement('button');
                button.className = 'button_Z9O button_ECf item_It6 item-mouse-states_Fmi item-focused_FuT button_xGY';
                button.style.cssText = `
                    padding: 8rem 12rem;
                    background: linear-gradient(180deg, #4a90e2 0%, #2e5c8a 100%);
                    color: white;
                    border: 1px solid rgba(255, 255, 255, 0.2);
                    border-radius: 4rem;
                    cursor: pointer;
                `;
                
                // Create icon
                const icon = document.createElement('img');
                icon.className = 'icon_Tdt icon_soN icon_Iwk';
                icon.src = 'coui://uil/Colored/Connection.svg';
                
                button.appendChild(icon);
                button.onclick = () => {
                    console.log("Manage Resource Chains clicked for entity:", selectedBuildingEntity);
                    // TODO: Open management panel
                };
                
                buttonContainer.appendChild(button);
                actionsSection.appendChild(buttonContainer);
                
                console.log("Button injected successfully!");
                
                // Clear interval after successful injection
                clearInterval((window as any).manageResourceChainsInterval);
                delete (window as any).manageResourceChainsInterval;
            };
            
            // Check immediately
            checkAndInject();
            
            // Set up polling
            (window as any).manageResourceChainsInterval = setInterval(checkAndInject, 100);
            
            // Clear after timeout
            setTimeout(() => {
                if ((window as any).manageResourceChainsInterval) {
                    clearInterval((window as any).manageResourceChainsInterval);
                    delete (window as any).manageResourceChainsInterval;
                }
            }, 5000);
            
        } else {
            // Remove button when no building is selected
            const existingButton = document.getElementById('manage-resource-chains-btn');
            if (existingButton) {
                console.log("Removing button from DOM");
                existingButton.remove();
            }
            
            // Clear any polling interval
            if ((window as any).manageResourceChainsInterval) {
                clearInterval((window as any).manageResourceChainsInterval);
                delete (window as any).manageResourceChainsInterval;
            }
        }
        
        return () => {
            // Cleanup on unmount
            const existingButton = document.getElementById('manage-resource-chains-btn');
            if (existingButton) {
                existingButton.remove();
            }
        };
    }, [isBuildingSelected, selectedBuildingEntity]);

    // This component doesn't render anything itself - it injects into the DOM
    return null;
};

