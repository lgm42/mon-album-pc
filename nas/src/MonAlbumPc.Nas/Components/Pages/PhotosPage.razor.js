// Photos page: tells the server which part of the grid is visible (it renders only those rows), forwards the keyboard
// shortcuts, and turns swipes in the viewer into previous/next.

const SizeKey = "monalbumpc.photos.tileSize";
let detach = null;

export function init(scroller, canvas, page) {
    dispose();
    let sent = { top: -1, height: -1, width: -1 };
    let frame = 0;
    let idle = 0;

    const report = force => {
        frame = 0;
        const top = scroller.scrollTop, height = scroller.clientHeight, width = canvas.clientWidth;
        const moved = Math.abs(top - sent.top);
        if (width === sent.width && height === sent.height && (force ? moved === 0 : moved < height / 4)) {
            return;
        }
        sent = { top, height, width };
        page.invokeMethodAsync("OnViewport", top, height, width);
    };
    const onScroll = () => {
        frame ||= requestAnimationFrame(() => report(false));
        clearTimeout(idle);
        idle = setTimeout(() => report(true), 120); // exact position once scrolling stops
    };
    const resized = new ResizeObserver(() => report(true));
    resized.observe(scroller);
    resized.observe(canvas);
    scroller.addEventListener("scroll", onScroll, { passive: true });

    const onKey = e => {
        const key = shortcut(e);
        if (key) {
            e.preventDefault();
            page.invokeMethodAsync("OnKey", key);
        }
    };
    document.addEventListener("keydown", onKey);

    let touch = null;
    const onTouchStart = e => {
        const stage = e.target.closest?.(".lightbox-stage");
        touch = stage && e.touches.length === 1 && e.target.tagName !== "VIDEO"
            ? { x: e.touches[0].clientX, y: e.touches[0].clientY }
            : null;
    };
    const onTouchEnd = e => {
        if (!touch || e.changedTouches.length !== 1) {
            return;
        }
        const dx = e.changedTouches[0].clientX - touch.x, dy = e.changedTouches[0].clientY - touch.y;
        touch = null;
        if (Math.abs(dx) > 60 && Math.abs(dx) > 2 * Math.abs(dy)) {
            page.invokeMethodAsync("OnKey", dx < 0 ? "ArrowRight" : "ArrowLeft");
        }
    };
    document.addEventListener("touchstart", onTouchStart, { passive: true });
    document.addEventListener("touchend", onTouchEnd, { passive: true });

    detach = () => {
        resized.disconnect();
        scroller.removeEventListener("scroll", onScroll);
        document.removeEventListener("keydown", onKey);
        document.removeEventListener("touchstart", onTouchStart);
        document.removeEventListener("touchend", onTouchEnd);
        cancelAnimationFrame(frame);
        clearTimeout(idle);
    };
    report(true);

    let size = null;
    try {
        size = parseInt(localStorage.getItem(SizeKey) ?? "", 10) || null;
    } catch {
        // storage blocked: default size
    }
    return { touch: matchMedia("(hover: none)").matches, tileSize: size };
}

export function scrollTo(scroller, top) {
    scroller.scrollTop = top;
}

export function saveTileSize(size) {
    try {
        localStorage.setItem(SizeKey, String(size));
    } catch {
        // not kept: fine
    }
}

export function dispose() {
    detach?.();
    detach = null;
}

// The keys the page handles, never while typing or while a dialog is open.
function shortcut(e) {
    if (e.altKey || e.isComposing || isTyping(e) || document.querySelector("fluent-dialog:not([hidden])")) {
        return null;
    }
    if ((e.ctrlKey || e.metaKey) && !e.shiftKey && e.key.toLowerCase() === "a") {
        return "SelectAll";
    }
    if (e.ctrlKey || e.metaKey) {
        return null;
    }
    switch (e.key) {
        case "ArrowLeft":
        case "ArrowRight":
        case "Escape":
        case "Delete":
            return e.key;
        case "Enter":
            return isControl(document.activeElement) ? null : e.key;
        default:
            return null;
    }
}

function isTyping(e) {
    const target = e.composedPath()[0];
    if (target instanceof HTMLElement && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName))) {
        return true;
    }
    return /^FLUENT-(TEXT-FIELD|TEXT-AREA|SEARCH|NUMBER-FIELD|SELECT|COMBOBOX|SLIDER)$/.test(document.activeElement?.tagName ?? "");
}

function isControl(element) {
    return !!element && /^(BUTTON|A|FLUENT-BUTTON|FLUENT-ANCHOR|FLUENT-OPTION|VIDEO)$/.test(element.tagName);
}
