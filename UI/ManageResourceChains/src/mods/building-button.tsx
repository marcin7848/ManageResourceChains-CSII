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
                
                // ALWAYS log analysis data for debugging, even if button exists
                console.log("=== BUILDING PANEL ANALYSIS ===");
                console.log("Entity:", selectedBuildingEntity);
                console.log("Actions section found:", actionsSection.className);
                console.log("Actions section children count:", actionsSection.children.length);
                console.log("Actions section HTML:", actionsSection.innerHTML.substring(0, 500));
                
                // Log all children to understand the structure
                console.log("Children details:");
                for (let i = 0; i < actionsSection.children.length; i++) {
                    const child = actionsSection.children[i];
                    console.log(`  Child ${i}:`, {
                        tag: child.tagName,
                        className: child.className,
                        id: child.id,
                        textContent: child.textContent?.substring(0, 50)
                    });
                }
                
                // Check if there are existing buttons
                const existingButtons = actionsSection.querySelectorAll('button');
                console.log("Existing buttons count:", existingButtons.length);
                existingButtons.forEach((btn, idx) => {
                    console.log(`  Button ${idx}:`, {
                        className: btn.className,
                        hasIcon: btn.querySelector('img') !== null,
                        iconSrc: btn.querySelector('img')?.src
                    });
                });
                
                // Check parent structure to understand different layouts
                const parentSection = actionsSection.parentElement;
                console.log("Parent section:", {
                    className: parentSection?.className,
                    childrenCount: parentSection?.children.length
                });
                console.log("=== END ANALYSIS ===");
                
                // Check if button already exists
                const existingButton = actionsSection.querySelector('#manage-resource-chains-btn');
                if (existingButton) {
                    console.log("Button already exists");
                    return;
                }
                
                console.log("Creating and injecting button...");
                
                // Create button directly without wrapper div, styled like FirstPersonCamera button
                const button = document.createElement('button');
                button.id = 'manage-resource-chains-btn';
                button.className = 'button_Z9O button_ECf item_It6 item-mouse-states_Fmi item-selected_tAM item-focused_FuT button_Z9O button_ECf item_It6 item-mouse-states_Fmi item-selected_tAM item-focused_FuT button_xGY';
                button.style.cssText = 'margin-left: 6rem; margin-right: 8rem;';
                
                // Create icon - use an icon that exists
                const icon = document.createElement('img');
                icon.className = 'icon_Tdt icon_soN icon_Iwk';
                icon.src = 'coui://uil/Standard/Link.svg';
                
                button.appendChild(icon);
                button.onclick = () => {
                    console.log("Manage Resource Chains clicked for entity:", selectedBuildingEntity);
                    // TODO: Open management panel
                };
                
                // Find the right insertion point - before any spacer/divider or right-aligned elements
                // Look for elements that are NOT buttons (likely spacers) or elements with specific classes
                let insertBeforeElement = null;
                for (let i = 0; i < actionsSection.children.length; i++) {
                    const child = actionsSection.children[i];
                    // If it's not a button, it's likely a spacer - insert before it
                    if (child.tagName !== 'BUTTON') {
                        insertBeforeElement = child;
                        console.log(`Found non-button element at index ${i}, will insert before it`);
                        break;
                    }
                }
                
                if (insertBeforeElement) {
                    console.log("Inserting button before spacer/divider element");
                    actionsSection.insertBefore(button, insertBeforeElement);
                } else {
                    console.log("No spacer found, appending to end of actions section");
                    actionsSection.appendChild(button);
                }
                
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

