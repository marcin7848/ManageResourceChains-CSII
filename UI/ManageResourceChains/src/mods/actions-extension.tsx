import { useValue } from "cs2/api";
import { bindValue } from "cs2/api";

// Bindings to our C# system
const isBuildingSelected$ = bindValue<boolean>("manageResourceChains", "isBuildingSelected", false);
const selectedBuildingEntity$ = bindValue<number>("manageResourceChains", "selectedBuildingEntity", 0);

// This function extends the ActionsSection to add our button
export const ManageResourceChainsActionButton = (Component: any) => {
    return (props: any) => {
        const isBuildingSelected = useValue(isBuildingSelected$);
        const selectedBuildingEntity = useValue(selectedBuildingEntity$);

        console.log("ManageResourceChainsActionButton - isBuildingSelected:", isBuildingSelected, "entity:", selectedBuildingEntity);

        const handleClick = () => {
            console.log("Manage Resource Chains clicked for entity:", selectedBuildingEntity);
            alert(`Manage Resource Chains\nEntity: ${selectedBuildingEntity}`);
        };

        // Render the original ActionsSection component
        const originalContent = Component(props);

        console.log("Original ActionsSection content:", originalContent);

        // If no building is selected, just return the original component
        if (!isBuildingSelected || selectedBuildingEntity === 0) {
            console.log("No building selected, returning original ActionsSection");
            return originalContent;
        }

        console.log("Building selected! Adding our button to ActionsSection");

        // Add our button alongside the original content
        return (
            <>
                {originalContent}
                <button 
                    style={{
                        background: '#4CAF50',
                        color: 'white',
                        border: 'none',
                        padding: '10px 20px',
                        margin: '5px',
                        fontSize: '14px',
                        fontWeight: 'bold',
                        borderRadius: '4px',
                        cursor: 'pointer'
                    }}
                    onClick={handleClick}
                >
                    🔗 Manage Resource Chains
                </button>
            </>
        );
    };
};

