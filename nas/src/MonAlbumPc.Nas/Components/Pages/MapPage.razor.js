// Map page: the Leaflet map of the located photos (points come from the server as integers, see MapModel.Encode), the
// visible part of the photo list beside it (as on the Photos page), and the keyboard shortcuts and swipes of the viewer.
//   server -> page: showPoints({generation, colors, points}, fit), highlight(id, tip), scrollTo(scroller, top)
//   page -> server: OnBounds, OnMarker, Tip, OnTiles, OnViewport, OnKey

const ViewKey = "monalbumpc.map.view";
let detach = null;
let map = null;
let markers = null;
let renderer = null;
let points = { generation: -1, byId: [] };
let ring = null;
let boundsChanged = () => {};

export async function init(element, scroller, canvas, page, scripts) {
    dispose();
    let disposed = false;
    detach = () => { disposed = true; };
    for (const src of scripts) {
        await loadScript(src);
    }
    if (disposed) {
        return null;
    }

    const saved = savedView();
    map = L.map(element, { preferCanvas: true, worldCopyJump: true, minZoom: 2 });
    if (saved) {
        map.setView(saved.center, saved.zoom);
    } else {
        map.setView([30, 0], 2);
    }

    // Tiles come straight from OpenStreetMap to the browser; its policy wants the attribution and a Referer.
    const tiles = L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        referrerPolicy: "strict-origin-when-cross-origin",
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">contributeurs OpenStreetMap</a>',
    }).addTo(map);
    watchTiles(tiles, page);

    renderer = L.canvas({ padding: 0.5 });
    markers = L.markerClusterGroup({
        chunkedLoading: true,
        showCoverageOnHover: false,
        spiderfyOnMaxZoom: true,
        maxClusterRadius: 60,
        iconCreateFunction: cluster => {
            const count = cluster.getChildCount();
            const size = count < 10 ? 32 : count < 100 ? 38 : count < 1000 ? 44 : 52;
            const label = count < 10000 ? String(count) : Math.round(count / 1000) + "k";
            return L.divIcon({ html: label, className: "cluster" + (count >= 100 ? " large" : ""), iconSize: [size, size] });
        },
    });
    map.addLayer(markers);
    map.createPane("highlight");
    map.getPane("highlight").style.zIndex = 650;
    map.getPane("highlight").style.pointerEvents = "none";

    // Tooltips are asked for on first hover only: thousands of markers load without any name or path.
    let hovered = null;
    const showTip = async (layer, id, count) => {
        hovered = layer;
        if (layer.getTooltip()) {
            layer.openTooltip();
            return;
        }
        const generation = points.generation;
        const tip = await page.invokeMethodAsync("Tip", generation, id);
        if (tip && hovered === layer && generation === points.generation) {
            layer.bindTooltip(tipHtml(tip, count), { direction: "top", offset: [0, count ? -16 : -6], className: "photo-tooltip" }).openTooltip();
        }
    };
    markers.on("mouseover", e => showTip(e.layer, e.layer.options.id, 0));
    markers.on("mouseout", () => { hovered = null; });
    markers.on("clustermouseover", e => {
        // The newest photo of the group stands for it (ids follow the date, newest first).
        const children = e.layer.getAllChildMarkers();
        let newest = children[0].options.id;
        for (const child of children) {
            newest = Math.min(newest, child.options.id);
        }
        showTip(e.layer, newest, e.layer.getChildCount());
    });
    markers.on("clustermouseout", () => { hovered = null; });
    // A new zoom level makes new groups: drop the tooltip of a group that no longer exists.
    markers.on("animationend", () => { hovered = null; });
    markers.on("click", e => page.invokeMethodAsync("OnMarker", points.generation, e.layer.options.id));

    let boundsTimer = 0;
    boundsChanged = () => {
        clearTimeout(boundsTimer);
        boundsTimer = setTimeout(() => {
            const b = map.getBounds();
            page.invokeMethodAsync("OnBounds", b.getSouth(), b.getWest(), b.getNorth(), b.getEast());
            saveView();
        }, 200);
    };
    map.on("moveend zoomend resize", boundsChanged);
    const mapResized = new ResizeObserver(() => map.invalidateSize());
    mapResized.observe(element);

    const stopGrid = watchGrid(scroller, canvas, page);
    const stopKeys = watchKeys(element, page);
    detach = () => {
        clearTimeout(boundsTimer);
        mapResized.disconnect();
        stopGrid();
        stopKeys();
        map.remove();
        map = markers = renderer = ring = null;
        boundsChanged = () => {};
        points = { generation: -1, byId: [] };
    };
    return { touch: matchMedia("(hover: none)").matches, savedView: !!saved };
}

export function showPoints(data, fit) {
    if (!map) {
        return;
    }
    markers.clearLayers();
    clearRing();
    const raw = data.points;
    const count = raw.length / 3;
    const byId = new Array(count);
    let south = 90, north = -90, west = 180, east = -180;
    for (let id = 0; id < count; id++) {
        const lat = raw[3 * id] / 1e5, lng = raw[3 * id + 1] / 1e5, flags = raw[3 * id + 2];
        byId[id] = L.circleMarker([lat, lng], {
            renderer,
            radius: 7,
            color: "#ffffff",
            weight: 2,
            fillColor: data.colors[flags >> 1] || "#3D4FD1",
            fillOpacity: 1,
            id,
        });
        south = Math.min(south, lat);
        north = Math.max(north, lat);
        west = Math.min(west, lng);
        east = Math.max(east, lng);
    }
    points = { generation: data.generation, byId };
    markers.addLayers(byId);
    if (fit) {
        if (count > 0) {
            map.fitBounds([[south, west], [north, east]], { padding: [40, 40], maxZoom: 14, animate: false });
        } else {
            map.setView([30, 0], 2, { animate: false });
        }
    }
    // The list beside the map follows the visible area, even when the view did not move.
    boundsChanged();
}

// A ring around the photo's place, without moving the map: the list beside it follows the visible area.
export function highlight(id, tip) {
    clearRing();
    const marker = map && id !== null && id !== undefined ? points.byId[id] : null;
    if (!marker) {
        return;
    }
    ring = L.circleMarker(marker.getLatLng(), {
        pane: "highlight",
        interactive: false,
        radius: 13,
        color: getComputedStyle(map.getContainer()).getPropertyValue("--accent-fill-rest").trim() || "#3D4FD1",
        weight: 4,
        fillColor: marker.options.fillColor,
        fillOpacity: 0.9,
    }).addTo(map);
    if (tip) {
        ring.bindTooltip(tipHtml(tip, 0), { permanent: true, direction: "top", offset: [0, -12], className: "photo-tooltip" }).openTooltip();
    }
}

export function scrollTo(scroller, top) {
    scroller.scrollTop = top;
}

export function dispose() {
    detach?.();
    detach = null;
}

function clearRing() {
    if (ring) {
        ring.remove();
        ring = null;
    }
}

function tipHtml(tip, count) {
    const thumb = `<img src="${escape(tip.thumb)}" alt="" width="160" height="120" onerror="this.style.visibility='hidden'">`;
    const title = count > 1 ? `${count.toLocaleString("fr-FR")} photos` : escape(tip.name);
    const date = count > 1 ? `La plus récente : ${escape(tip.date)}` : escape(tip.date) + (tip.video ? " · Vidéo" : "");
    return `<div class="photo-tip">${thumb}<div class="name">${title}</div><div class="date">${date}</div></div>`;
}

function escape(text) {
    return String(text).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

// Tells the page when the base map does not load at all (no Internet on this device, OpenStreetMap down), instead of
// leaving a grey map.
function watchTiles(tiles, page) {
    let loaded = 0, failed = 0, failing = false;
    tiles.on("tileload", () => loaded++);
    tiles.on("tileerror", () => failed++);
    tiles.on("load", () => {
        const now = failed > 0 && loaded === 0 ? true : loaded > 0 ? false : failing;
        loaded = failed = 0;
        if (now !== failing) {
            failing = now;
            page.invokeMethodAsync("OnTiles", failing);
        }
    });
}

// The server renders only the rows of the list near the visible part (see PhotoGridLayout).
function watchGrid(scroller, canvas, page) {
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
        idle = setTimeout(() => report(true), 120);
    };
    const resized = new ResizeObserver(() => report(true));
    resized.observe(scroller);
    resized.observe(canvas);
    scroller.addEventListener("scroll", onScroll, { passive: true });
    report(true);
    return () => {
        resized.disconnect();
        scroller.removeEventListener("scroll", onScroll);
        cancelAnimationFrame(frame);
        clearTimeout(idle);
    };
}

function watchKeys(element, page) {
    const onKey = e => {
        if (e.altKey || e.ctrlKey || e.metaKey || e.isComposing || isTyping(e)) {
            return;
        }
        const viewing = !!document.querySelector(".lightbox");
        // Arrow keys on the map move the map.
        if (!viewing && element.contains(e.target)) {
            return;
        }
        if (["ArrowLeft", "ArrowRight", "Escape"].includes(e.key) || (e.key === "Enter" && !viewing && !isControl(document.activeElement))) {
            e.preventDefault();
            // The map may still have the focus under the viewer: its own arrow keys would move it.
            e.stopPropagation();
            page.invokeMethodAsync("OnKey", e.key);
        }
    };
    let touch = null;
    const onTouchStart = e => {
        const stage = e.target.closest?.(".lightbox-stage");
        touch = stage && e.touches.length === 1 && e.target.tagName !== "VIDEO" ? { x: e.touches[0].clientX, y: e.touches[0].clientY } : null;
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
    document.addEventListener("keydown", onKey, true);
    document.addEventListener("touchstart", onTouchStart, { passive: true });
    document.addEventListener("touchend", onTouchEnd, { passive: true });
    return () => {
        document.removeEventListener("keydown", onKey, true);
        document.removeEventListener("touchstart", onTouchStart);
        document.removeEventListener("touchend", onTouchEnd);
    };
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

function savedView() {
    try {
        const view = JSON.parse(localStorage.getItem(ViewKey) ?? "null");
        return view && Array.isArray(view.center) && Number.isFinite(view.zoom) ? view : null;
    } catch {
        return null; // storage blocked or garbled: fit to the photos
    }
}

function saveView() {
    try {
        const center = map.getCenter();
        localStorage.setItem(ViewKey, JSON.stringify({ center: [center.lat, center.lng], zoom: map.getZoom() }));
    } catch {
        // not kept: fine
    }
}

// Leaflet and its cluster plugin are classic scripts that define the global L; loaded once per tab.
function loadScript(src) {
    const url = new URL(src, document.baseURI).href;
    const existing = [...document.scripts].find(s => s.src === url);
    if (existing?.dataset.loaded) {
        return Promise.resolve();
    }
    return new Promise((resolve, reject) => {
        const script = existing ?? document.createElement("script");
        script.addEventListener("load", () => { script.dataset.loaded = "1"; resolve(); }, { once: true });
        script.addEventListener("error", () => { script.remove(); reject(new Error("Script introuvable : " + src)); }, { once: true });
        if (!existing) {
            script.src = url;
            document.head.appendChild(script);
        }
    });
}
