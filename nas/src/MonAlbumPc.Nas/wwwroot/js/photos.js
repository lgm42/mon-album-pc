// Viewer helpers: grid width (to lay out rows of thumbnails) and keyboard shortcuts.

const keys = ["ArrowLeft", "ArrowRight", "Enter", "Escape", "Delete"];

function typingOrDialog(target) {
    if (target instanceof Element && target.closest("input, textarea, select, fluent-text-field, fluent-select, fluent-slider, video")) {
        return true;
    }
    // A dialog (confirmation, wizard) is open: leave the keys to it.
    return document.querySelector("fluent-dialog:not([hidden])") !== null;
}

export function init(grid, dotnet) {
    let last = 0;
    const observer = new ResizeObserver(() => {
        const width = Math.floor(grid.clientWidth);
        if (width !== last) {
            last = width;
            dotnet.invokeMethodAsync("OnGridWidth", width);
        }
    });
    observer.observe(grid);

    const onKey = (e) => {
        if (!keys.includes(e.key) || e.ctrlKey || e.altKey || e.metaKey || typingOrDialog(e.target)) {
            return;
        }
        e.preventDefault();
        dotnet.invokeMethodAsync("OnKey", e.key);
    };
    document.addEventListener("keydown", onKey);

    return {
        dispose() {
            observer.disconnect();
            document.removeEventListener("keydown", onKey);
        },
    };
}

export function reveal(id) {
    document.getElementById(id)?.scrollIntoView({ block: "nearest" });
}
