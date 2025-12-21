import { getModule } from "cs2/modding";
import { useValue, trigger } from "cs2/api";
import { Button } from "cs2/ui";

// Get vanilla components from the game
const InfoSection: any = getModule( 
    "game-ui/game/components/selected-info-panel/shared-components/info-section/info-section.tsx",
    "InfoSection"
);

const InfoRow: any = getModule(
    "game-ui/game/components/selected-info-panel/shared-components/info-row/info-row.tsx",
    "InfoRow"
);

interface InfoSectionComponent {
    group: string;
    tooltipKeys: Array<string>;
    tooltipTags: Array<string>;
}

function openMenu() {
    console.log("Opening Resource Chains menu");
    trigger('manageResourceChains', 'openMenu');
}

// Component that renders the Resource Chains section
const ResourceChainsSection = () => {
    console.log("ResourceChainsSection rendering!");
    return (
        <InfoSection title="Resource Chains">
            <InfoRow 
                left="Building Resources"
                right={
                    <Button 
                        variant="icon"
                        onSelect={() => openMenu()}
                    >
                        🏭
                    </Button>
                }
            />
        </InfoSection>
    );
};

// Extension function that adds our section to the selected info panel
export const ResourceChainsInfoSection = (componentList: any): any => {
    console.log("===== ResourceChainsInfoSection CALLED =====");
    console.log("Component list type:", typeof componentList);
    console.log("Component list keys BEFORE:", Object.keys(componentList).join(','));
    
    // The key must match the C# system's full namespace and class name
    componentList["ManageResourceChains.Systems.ResourceChainsInfoSectionSystem"] = (e: InfoSectionComponent) => {
        console.log("===== Component function CALLED for ResourceChainsInfoSectionSystem =====");
        console.log("Props:", JSON.stringify(e));
        return <ResourceChainsSection />;
    };

    console.log("Component list keys AFTER:", Object.keys(componentList).join(','));
    console.log("Added key:", "ManageResourceChains.Systems.ResourceChainsInfoSectionSystem");
    console.log("Function type:", typeof componentList["ManageResourceChains.Systems.ResourceChainsInfoSectionSystem"]);
    
    return componentList;
};

