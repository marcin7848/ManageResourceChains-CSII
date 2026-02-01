import { ModRegistrar } from "cs2/modding";
import { BuildingButton, DistrictButton } from "mods/building-button";

const register: ModRegistrar = (moduleRegistry) => {
    console.log("===== ManageResourceChains UI: Starting registration =====");
    console.log("Module registry:", moduleRegistry);
    
    // Add our button to the game UI - it will position itself when a building is selected
    console.log("ManageResourceChains: About to append BuildingButton to Game...");
    try {
        moduleRegistry.append('Game', BuildingButton);
        console.log("ManageResourceChains: Successfully appended BuildingButton to Game!");
    } catch (error) {
        console.error("ManageResourceChains: ERROR appending BuildingButton:", error);
    }
    
    // Add district button
    console.log("ManageResourceChains: About to append DistrictButton to Game...");
    try {
        moduleRegistry.append('Game', DistrictButton);
        console.log("ManageResourceChains: Successfully appended DistrictButton to Game!");
    } catch (error) {
        console.error("ManageResourceChains: ERROR appending DistrictButton:", error);
    }
    
    console.log("===== ManageResourceChains UI: Registration complete =====");
}

export default register;