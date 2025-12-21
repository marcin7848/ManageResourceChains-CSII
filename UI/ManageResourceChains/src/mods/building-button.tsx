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
            console.log("BuildingButton: Injecting button into DOM");
            
            // Try multiple selectors to find the building panel
            const selectors = [
                '.infoview-panel-section_RXJ .content_1xS',  // Original attempt
                '[class*="infoview-panel-section"] [class*="content"]',  // Partial match
                '[class*="selected-info-panel"]',  // Selected info panel
                '.game-info-panel',  // Generic info panel
                '[class*="info-panel"]'  // Any info panel
            ];
            
            let targetSection = null;
            for (const selector of selectors) {
                targetSection = document.querySelector(selector);
                if (targetSection) {
                    console.log(`Found section using selector: ${selector}`);
                    break;
                }
            }
            
            // If still not found, try to find ANY panel-like div
            if (!targetSection) {
                console.log("Standard selectors failed, searching for panel elements...");
                const allDivs = document.querySelectorAll('div[class*="panel"]');
                console.log(`Found ${allDivs.length} divs with 'panel' in className`);
                if (allDivs.length > 0) {
                    // Log first few to help debug
                    for (let i = 0; i < Math.min(5, allDivs.length); i++) {
                        console.log(`Panel ${i}: ${allDivs[i].className}`);
                    }
                }
            }
            
            if (targetSection && !document.getElementById('manage-resource-chains-btn')) {
                console.log("Found target section, creating button");
                
                const buttonContainer = document.createElement('div');
                buttonContainer.id = 'manage-resource-chains-btn';
                buttonContainer.style.cssText = 'margin: 8rem 0; padding: 8rem;';
                
                const button = document.createElement('button');
                button.className = 'button_Z9O button_ECf item_It6 item-mouse-states_Fmi item-focused_FuT';
                button.style.cssText = `
                    width: 100%;
                    padding: 12rem 16rem;
                    background: linear-gradient(180deg, #4a90e2 0%, #2e5c8a 100%);
                    color: white;
                    border: 2px solid rgba(255, 255, 255, 0.3);
                    border-radius: 6rem;
                    font-size: 16rem;
                    font-weight: 700;
                    cursor: pointer;
                    text-transform: uppercase;
                    letter-spacing: 1px;
                `;
                button.textContent = '🔗 MANAGE RESOURCE CHAINS';
                button.onclick = () => {
                    console.log("Manage Resource Chains clicked for entity:", selectedBuildingEntity);
                    // TODO: Open management panel
                };
                
                buttonContainer.appendChild(button);
                targetSection.appendChild(buttonContainer);
                
                console.log("Button injected successfully into:", targetSection.className);
            } else if (!targetSection) {
                console.log("ERROR: Could not find any suitable panel section in DOM");
            }
        } else {
            // Remove button when no building is selected
            const existingButton = document.getElementById('manage-resource-chains-btn');
            if (existingButton) {
                console.log("Removing button from DOM");
                existingButton.remove();
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

