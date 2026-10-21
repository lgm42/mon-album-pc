// Map of the located photos, driven by the Windows app through WebView2 messages.
//   app -> page: {type: "points", colors: [...], points: [[id, lat, lon, colorIndex, name, date], ...], fit: bool}
//                {type: "highlight", id: number|null}
//   page -> app: {type: "ready"}, {type: "bounds", south, west, north, east}, {type: "select", id}
(function () {
    "use strict";

    const host = window.chrome && window.chrome.webview;
    const post = message => host && host.postMessage(message);

    const map = L.map("map", {
        preferCanvas: true,
        worldCopyJump: true,
        zoomControl: true,
        minZoom: 2,
    }).setView([30, 0], 2);

    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        referrerPolicy: "strict-origin-when-cross-origin",
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">contributeurs OpenStreetMap</a>',
    }).addTo(map);

    const renderer = L.canvas({ padding: 0.5 });
    const markers = L.markerClusterGroup({
        chunkedLoading: true,
        showCoverageOnHover: false,
        spiderfyOnMaxZoom: true,
        maxClusterRadius: 60,
        iconCreateFunction: cluster => {
            const count = cluster.getChildCount();
            const size = count < 10 ? 32 : count < 100 ? 38 : count < 1000 ? 44 : 52;
            const label = count < 10000 ? String(count) : Math.round(count / 1000) + "k";
            return L.divIcon({
                html: label,
                className: "cluster" + (count >= 100 ? " large" : ""),
                iconSize: [size, size],
            });
        },
    });
    map.addLayer(markers);

    map.createPane("highlight");
    map.getPane("highlight").style.zIndex = 650;
    map.getPane("highlight").style.pointerEvents = "none";
    let highlight = null;

    const byId = new Map();
    const escape = text => String(text).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const tooltipHtml = p => `<div class="photo-tip"><div class="name">${escape(p[4])}</div><div class="date">${escape(p[5])}</div></div>`;

    // Tooltips are built on first hover only: tens of thousands of markers must load quickly.
    markers.on("mouseover", e => {
        const marker = e.layer;
        if (!marker.getTooltip()) {
            marker.bindTooltip(tooltipHtml(marker.options.photo), { direction: "top", offset: [0, -6] });
        }
        marker.openTooltip();
    });
    markers.on("click", e => post({ type: "select", id: e.layer.options.photo[0] }));

    let boundsTimer = 0;
    function postBounds() {
        clearTimeout(boundsTimer);
        boundsTimer = setTimeout(() => {
            const b = map.getBounds();
            post({ type: "bounds", south: b.getSouth(), west: b.getWest(), north: b.getNorth(), east: b.getEast() });
        }, 150);
    }
    map.on("moveend zoomend resize", postBounds);

    function showPoints(message) {
        markers.clearLayers();
        byId.clear();
        clearHighlight();
        const colors = message.colors || [];
        const layers = new Array(message.points.length);
        for (let i = 0; i < message.points.length; i++) {
            const p = message.points[i];
            const marker = L.circleMarker([p[1], p[2]], {
                renderer,
                radius: 7,
                color: "#ffffff",
                weight: 2,
                fillColor: colors[p[3]] || "#3D4FD1",
                fillOpacity: 1,
                photo: p,
            });
            byId.set(p[0], marker);
            layers[i] = marker;
        }
        markers.addLayers(layers);
        document.getElementById("empty").hidden = layers.length > 0;
        if (message.fit) {
            if (layers.length > 0) {
                const bounds = L.latLngBounds(message.points.map(p => [p[1], p[2]]));
                map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14, animate: false });
            } else {
                map.setView([30, 0], 2, { animate: false });
            }
        }
        postBounds();
    }

    function clearHighlight() {
        if (highlight) {
            map.removeLayer(highlight);
            highlight = null;
        }
    }

    // A ring around the photo's place, without moving the map (the list on the right follows the visible area).
    function showHighlight(id) {
        clearHighlight();
        const marker = id === null || id === undefined ? null : byId.get(id);
        if (!marker) {
            return;
        }
        highlight = L.circleMarker(marker.getLatLng(), {
            pane: "highlight",
            interactive: false,
            radius: 13,
            color: "#3D4FD1",
            weight: 4,
            fill: true,
            fillColor: marker.options.fillColor,
            fillOpacity: 0.9,
        }).bindTooltip(tooltipHtml(marker.options.photo), { permanent: true, direction: "top", offset: [0, -12] }).addTo(map);
    }

    if (host) {
        host.addEventListener("message", e => {
            const message = e.data;
            if (message.type === "points") {
                showPoints(message);
            } else if (message.type === "highlight") {
                showHighlight(message.id);
            }
        });
    }
    post({ type: "ready" });
})();
