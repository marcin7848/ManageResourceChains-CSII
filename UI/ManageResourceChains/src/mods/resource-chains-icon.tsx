import { useState } from "react";
import { useValue } from "cs2/api";
import { Button, Panel } from "cs2/ui";
import { selectedInfo } from "cs2/bindings";
import { openMenu } from "./bindings";
import styles from "./resource-chains-icon.module.scss";

export const ResourceChainsIcon = () => {
    const [isMenuOpen, setIsMenuOpen] = useState(false);
    const selectedEntity = useValue(selectedInfo.selectedEntity$);

    // Check if an entity is selected (index > 0 means valid entity)
    const isEntitySelected = selectedEntity && selectedEntity.index > 0;

    if (!isEntitySelected) {
        return null;
    }

    const handleIconClick = () => {
        setIsMenuOpen(!isMenuOpen);
        openMenu();
    };

    return (
        <>
            <div className={styles.iconContainer}>
                <Button 
                    variant="round"
                    onSelect={handleIconClick}
                    className={styles.iconButton}
                    selected={isMenuOpen}
                >
                    🏭
                </Button>
            </div>

            {isMenuOpen && (
                <Panel
                    className={styles.menuPanel}
                    header="Resource Chains"
                    onClose={() => setIsMenuOpen(false)}
                >
                    <div className={styles.menuContent}>
                        <h2>Manage Resource Chains</h2>
                        <p>Building information and resource chain management will appear here.</p>
                        <p>Selected Entity: {selectedEntity.index}</p>
                    </div>
                </Panel>
            )}
        </>
    );
};

