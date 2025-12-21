import { bindValue, trigger } from "cs2/api";
import mod from "../../mod.json";

// We'll use the game's built-in selectedEntity$ from cs2/bindings instead of creating our own
export function openMenu() {
    trigger(mod.id, 'openMenu');
}

