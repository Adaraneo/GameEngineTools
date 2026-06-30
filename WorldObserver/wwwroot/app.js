// app.js — World Observer realtime dashboard client
// All dynamic content is inserted via textContent / DOM APIs (no innerHTML),
// so engine-supplied names and narrative text can never be interpreted as markup.
"use strict";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/world")
    .withAutomaticReconnect()
    .build();

const $ = (id) => document.getElementById(id);
const SVGNS = "http://www.w3.org/2000/svg";

// ── Tiny DOM helpers ─────────────────────────────────────────────────────────
function el(tag, opts = {}, children = []) {
    const node = document.createElement(tag);
    if (opts.class) node.className = opts.class;
    if (opts.text != null) node.textContent = opts.text;
    if (opts.style) node.setAttribute("style", opts.style);
    for (const c of children) node.appendChild(c);
    return node;
}
function svgEl(tag, attrs) {
    const node = document.createElementNS(SVGNS, tag);
    for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
    return node;
}
function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }

// ── Shared state ──────────────────────────────────────────────────────────────
const nameById = new Map();
const locNameById = new Map();
let selectedId = null;
let last = { characters: [], edges: [], statusById: new Map() };
let lastState = null;          // most recent full pushed state (for map mode re-render)
let lastEdgeMap = new Map();   // from|to -> edge (current tick)
let prevEdgeMap = new Map();   // from|to -> edge (one tick earlier, for trend arrows)
const relHist = new Map();     // from|to -> [development score samples] (for trend + forecast)
const REL_HIST_CAP = 60;
let lastCharById = new Map();  // id -> character (current tick)
let prevCharById = new Map();  // id -> character (one tick earlier, for trend arrows)

// Per-selected-character time-series history for the live charts.
const HISTORY_MAX = 120;
let history = [];
let histId = null;

const edgeKey = (e) => e.from + "|" + e.to;
const isDead = (id) => last.statusById.get(id) === "Dead";
// Display name of a character's partner (KinRole == Partner), or null.
const partnerNameOf = (id) => {
    for (const e of last.edges) {
        if (e.from === id && e.kinRole === "Partner") return nameById.get(e.to) || e.to.slice(0, 8);
    }
    return null;
};

// "Development" of a relationship: a 0..100 composite of bond depth (closeness/familiarity/
// trust dominate) plus commitment, communal strength, and accumulated positive history.
// Used to sort a character's relationships from most- to least-developed.
function relDevelopment(e) {
    const v = (x) => (typeof x === "number" ? x : 0);
    return 0.30 * v(e.closeness)
         + 0.20 * v(e.familiarity)
         + 0.20 * v(e.trust)
         + 0.10 * v(e.commitment)
         + 0.10 * v(e.communalStrength)
         + 0.10 * Math.min(100, v(e.positiveInteractions) * 4);
}

// Smoothed short-term trend of a relationship's development (least-squares slope over recent samples).
function relTrend(k) {
    const h = relHist.get(k);
    if (!h || h.length < 4) return { slope: 0, dir: "flat", delta: 0 };
    const n = Math.min(h.length, 16);
    const recent = h.slice(-n);
    let sx = 0, sy = 0, sxx = 0, sxy = 0;
    recent.forEach((y, i) => { sx += i; sy += y; sxx += i * i; sxy += i * y; });
    const denom = n * sxx - sx * sx;
    const slope = denom === 0 ? 0 : (n * sxy - sx * sy) / denom;
    const delta = recent[recent.length - 1] - recent[0];
    const dir = slope > 0.15 ? "up" : slope < -0.15 ? "down" : "flat";
    return { slope, dir, delta };
}

// Forecast a relationship's near-term outcome from the Rusbult model (commitment / investment /
// alternatives) + repair signals (transgression, contempt) + the development trend. All inputs
// already exist on the edge snapshot — this is inference, not new engine state.
function relForecast(e, trend) {
    const v = (x) => (typeof x === "number" ? x : 0);
    if (e.contemptuouslyDestroyed) return { text: "💔 nevratné pohrdání", cls: "fc-bad" };
    if (e.dissolutionConsidered) return { text: "⚠ ohrožený (zvažuje rozchod)", cls: "fc-bad" };
    if (v(e.commitment) < 25 && v(e.alternativeQuality) > 60) return { text: "⚠ křehký (lákají alternativy)", cls: "fc-bad" };
    if (v(e.transgressionResidue) > 40) return { text: "🩹 potřebuje usmíření", cls: "fc-warn" };
    if (v(e.commitment) > 60 && v(e.investmentSize) > 50 && v(e.alternativeQuality) < 40)
        return { text: "🔒 stabilní / oddaný", cls: "fc-good" };
    if (trend.dir === "up" && (v(e.responsiveDesire) > 40 || v(e.intimateAffinity) > 40 || v(e.sexualInterest) > 40))
        return { text: "💗 možný romantický vývoj", cls: "fc-good" };
    if (trend.dir === "up") return { text: "📈 prohlubuje se", cls: "fc-good" };
    if (trend.dir === "down") return { text: "📉 ochlazuje", cls: "fc-warn" };
    return { text: "➡ stabilní", cls: "fc-flat" };
}

// ── Controls ──────────────────────────────────────────────────────────────────
const conn = $("conn");
$("btnPlay").addEventListener("click", () => connection.invoke("Play"));
$("btnPause").addEventListener("click", () => connection.invoke("Pause"));
$("btnStep").addEventListener("click", () => connection.invoke("Step"));

const speed = $("speed");
speed.addEventListener("input", () => {
    $("speedVal").textContent = speed.value + " ms";
    connection.invoke("SetDelay", parseInt(speed.value, 10));
});

const tickmin = $("tickmin");
tickmin.addEventListener("input", () => {
    $("tickminVal").textContent = tickmin.value + " min/tik";
    connection.invoke("SetTickMinutes", parseInt(tickmin.value, 10));
});

// ── Character export / import (folder picked via the File System Access API) ──────
const toastEl = $("toast");
let toastTimer = null;
function toast(msg) {
    toastEl.textContent = msg;
    toastEl.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { toastEl.hidden = true; }, 4500);
}

async function exportCharacters() {
    let files;
    try {
        const res = await fetch("/api/characters/export");
        if (!res.ok) { toast("Export: svět ještě není připraven."); return; }
        files = await res.json();
    } catch (e) { toast("Export selhal: " + e.message); return; }
    if (!files.length) { toast("Žádné postavy k exportu."); return; }

    if (window.showDirectoryPicker) {
        try {
            const dir = await window.showDirectoryPicker({ mode: "readwrite", id: "wo-characters" });
            for (const f of files) {
                const h = await dir.getFileHandle(f.fileName, { create: true });
                const w = await h.createWritable();
                await w.write(f.json);
                await w.close();
            }
            toast(`Exportováno ${files.length} postav do vybrané složky.`);
        } catch (e) { if (e.name !== "AbortError") toast("Export selhal: " + e.message); }
    } else {
        // Fallback (non-Chromium): download a single bundle the fallback import can re-read.
        const blob = new Blob([JSON.stringify(files, null, 2)], { type: "application/json" });
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = "world-characters.json";
        a.click();
        URL.revokeObjectURL(a.href);
        toast(`Staženo ${files.length} postav (bundle) — prohlížeč nepodporuje výběr složky.`);
    }
}

async function postImport(files, replace) {
    if (!files.length) { toast("Žádné platné soubory postav."); return; }
    try {
        const res = await fetch("/api/characters/import?replace=" + (replace ? "true" : "false"), {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(files),
        });
        const r = await res.json();
        toast(`${replace ? "Nahrazeno" : "Import"}: přijato ${r.accepted} postav (objeví se v příštích ticích).`);
    } catch (e) { toast("Import selhal: " + e.message); }
}

// Accepts either individual CharacterData files or an exported bundle (array of {fileName, json}).
async function readImportFile(file) {
    const text = await file.text();
    try {
        const parsed = JSON.parse(text);
        if (Array.isArray(parsed)) return parsed.filter((e) => e && e.json).map((e) => ({ fileName: e.fileName || file.name, json: e.json }));
    } catch { /* not a bundle — treat as a single character */ }
    return [{ fileName: file.name, json: text }];
}

let fallbackReplace = false;
async function importCharacters(replace) {
    if (replace && !confirm("Nahradit celý svět postavami ze složky? Všechny stávající postavy budou odstraněny.")) return;
    if (window.showDirectoryPicker) {
        try {
            const dir = await window.showDirectoryPicker({ id: "wo-characters" });
            const files = [];
            for await (const handle of dir.values()) {
                if (handle.kind === "file" && handle.name.toLowerCase().endsWith(".json")) {
                    files.push(...(await readImportFile(await handle.getFile())));
                }
            }
            await postImport(files, replace);
        } catch (e) { if (e.name !== "AbortError") toast("Import selhal: " + e.message); }
    } else {
        fallbackReplace = replace;
        $("importFiles").click(); // fallback: multi-file picker
    }
}

$("btnExport").addEventListener("click", exportCharacters);
$("btnImport").addEventListener("click", () => importCharacters(false));
$("btnReplace").addEventListener("click", () => importCharacters(true));
$("importFiles").addEventListener("change", async (ev) => {
    const picked = Array.from(ev.target.files || []);
    const files = [];
    for (const file of picked) {
        if (file.name.toLowerCase().endsWith(".json")) files.push(...(await readImportFile(file)));
    }
    ev.target.value = "";
    const replace = fallbackReplace; fallbackReplace = false;
    await postImport(files, replace);
});

function reflectPaused(paused) {
    $("btnStep").disabled = !paused;
    $("btnPlay").style.borderColor = paused ? "" : "var(--accent)";
    $("btnPause").style.borderColor = paused ? "var(--accent)" : "";
}

// ── Color helpers ───────────────────────────────────────────────────────────────
function heat(value) {              // 0 green → 100 red ("more is worse")
    const v = Math.max(0, Math.min(100, value));
    return `hsl(${120 - (v / 100) * 120}, 65%, 50%)`;
}
function fuel(value) { return heat(100 - value); } // "more is better"

// Short Czech label for sexual orientation.
function orientLabel(o) {
    switch (o) {
        case "Heterosexual": return "hetero";
        case "Homosexual": return "homo";
        case "Bisexual": return "bi";
        case "Asexual": return "asex";
        default: return "?";
    }
}

function barBlock(label, value, colorFn) {
    const v = Math.max(0, Math.min(100, value));
    const lbl = el("div", { class: "bar-label" }, [
        el("span", { text: label }),
        el("span", { text: value.toFixed(0) }),
    ]);
    const fill = el("span", { style: `width:${v}%;background:${colorFn(value)}` });
    return [lbl, el("div", { class: "bar" }, [fill])];
}

// ── Vitals ──────────────────────────────────────────────────────────────────────
// ── Custom hover tooltip (instant, styled, follows the cursor, live values) ──────
const tooltip = el("div", { class: "tooltip" });
tooltip.style.display = "none";
document.body.appendChild(tooltip);
let hoveredCardId = null;
let lastMouse = { x: 0, y: 0 };

function positionTooltip() {
    const pad = 14;
    const r = tooltip.getBoundingClientRect();
    let x = lastMouse.x + pad;
    let y = lastMouse.y + pad;
    if (x + r.width > window.innerWidth) x = lastMouse.x - r.width - pad;
    if (y + r.height > window.innerHeight) y = lastMouse.y - r.height - pad;
    tooltip.style.left = Math.max(0, x) + "px";
    tooltip.style.top = Math.max(0, y) + "px";
}

function fillTooltip(c) {
    clear(tooltip);
    const h = el("div", { class: "tt-name", text: `${c.name} ${c.surname}`.trim() });
    h.appendChild(el("span", { class: "tt-id", text: " #" + c.id.slice(0, 8) }));
    tooltip.appendChild(h);
    tooltip.appendChild(el("div", { class: "tt-sub", text: `${c.sex} · ${orientLabel(c.orientation)} · ${c.age} let${c.occupation ? " · " + c.occupation : ""}` }));
    tooltip.appendChild(el("div", { class: "tt-row", text: `Domov: ${c.homeLocation || "—"}` }));
    tooltip.appendChild(el("div", { class: "tt-row", text: `Lokace: ${c.location} · Akce: ${c.currentAction || "—"}` }));
    if (c.travelingTo) tooltip.appendChild(el("div", { class: "tt-row", text: `🚶 Na cestě do: ${c.travelingTo}` }));
    tooltip.appendChild(el("div", { class: "tt-h", text: "Osobnost (Big Five)" }));
    const pe = c.personality || {};
    const grid = el("div", { class: "tt-ocean" });
    for (const [lbl, v] of [
        ["Otevřenost", pe.openness], ["Svědomitost", pe.conscientiousness],
        ["Extraverze", pe.extraversion], ["Přívětivost", pe.agreeableness], ["Neuroticismus", pe.neuroticism],
    ]) {
        const row = el("div", { class: "tt-dim" });
        row.appendChild(el("span", { class: "k", text: lbl }));
        const bar = el("div", { class: "tt-bar" }, [el("span", { style: `width:${Math.round((v || 0) * 100)}%` })]);
        row.appendChild(bar);
        row.appendChild(el("span", { class: "v", text: (v ?? 0).toFixed(2) }));
        grid.appendChild(row);
    }
    tooltip.appendChild(grid);
}

function showTooltip(c) { fillTooltip(c); tooltip.style.display = "block"; positionTooltip(); }
function hideTooltip() { tooltip.style.display = "none"; }

// Card elements are reused across ticks (keyed by id) and updated in place — avoids DOM churn and
// lets the hover tooltip stay anchored to a stable element.
const cardCache = new Map();

function renderVitals(characters) {
    const root = $("vitals");
    const seen = new Set();

    for (const c of characters) {
        nameById.set(c.id, c.name);
        seen.add(c.id);

        let e = cardCache.get(c.id);
        if (!e) {
            const card = el("div", { class: "card" });
            const id = c.id; // stable for this card across ticks
            card.addEventListener("click", () => selectNode(id));
            card.addEventListener("mouseenter", () => {
                hoveredCardId = id;
                const cur = lastCharById.get(id);
                if (cur) showTooltip(cur);
            });
            card.addEventListener("mousemove", (ev) => {
                lastMouse = { x: ev.clientX, y: ev.clientY };
                if (hoveredCardId === id) positionTooltip();
            });
            card.addEventListener("mouseleave", () => {
                if (hoveredCardId === id) { hoveredCardId = null; hideTooltip(); }
            });
            const emotion = el("span", { class: "emotion" });
            const name = el("div", { class: "name" });
            const sub = el("div", { class: "sub" });
            const bars = el("div", { class: "bars-inner" });
            card.appendChild(emotion);
            card.appendChild(name);
            card.appendChild(sub);
            card.appendChild(bars);
            root.appendChild(card);
            e = { card, emotion, name, sub, bars };
            cardCache.set(c.id, e);
        }

        let cls = "card";
        if (c.status === "Dead") cls += " dead";
        if (c.id === selectedId) cls += " selected";
        e.card.className = cls;
        e.emotion.textContent = c.emotion;
        if (hoveredCardId === c.id) showTooltip(c); // live-refresh values while hovering

        clear(e.name);
        e.name.appendChild(document.createTextNode(`${c.name} ${c.surname}`.trim()));
        e.name.appendChild(el("span", { class: "cid", text: " #" + c.id.slice(0, 8) }));
        const partner = partnerNameOf(c.id);
        if (partner) e.name.appendChild(el("span", { class: "partner-badge", text: "♥ " + partner }));

        let sub = `${c.sex} · ${orientLabel(c.orientation)}, ${c.age} let${c.occupation ? " · " + c.occupation : ""} · ${c.status}`;
        if (c.status === "Dead" && c.deathCause) sub += ` (${c.deathCause})`;
        if (c.travelingTo) sub += ` · 🚶 → ${c.travelingTo}`;
        e.sub.textContent = sub;

        clear(e.bars);
        for (const [lbl, val, fn] of [
            ["Stres", c.stress, heat],
            ["Nálada", c.moodBaseline, fuel],
            ["Energie", c.energy, fuel],
            ["Hlad", c.hunger, heat],
            ["Žízeň", c.thirst, heat],
        ]) {
            for (const n of barBlock(lbl, val, fn)) e.bars.appendChild(n);
        }
    }

    // Drop cards for characters no longer present.
    for (const [id, e] of cardCache) {
        if (!seen.has(id)) { e.card.remove(); cardCache.delete(id); }
    }
}

// ── Map (switchable: realistic road-network graph / grid / list) ────────────────
const MAP_W = 800, MAP_H = 560;
let mapLayout = new Map();     // locationId -> {x, y}
let mapStaticKey = "";         // "<mode>|<locationKey>" — rebuilds the static layer when it changes
const mapDots = new Map();     // characterId -> <g> dot element (reused so CSS transitions animate)
let mapMode = localStorage.getItem("wo.mapMode") || "graph";

// Grid layout: locations sorted by id into a stable grid.
function gridLayout(locs) {
    const layout = new Map();
    const n = locs.length || 1;
    const cols = Math.max(1, Math.ceil(Math.sqrt(n * (MAP_W / MAP_H))));
    const mx = 60, my = 30;
    const cw = (MAP_W - 2 * mx) / Math.max(1, cols - 1 || 1);
    const ch = (MAP_H - 2 * my) / Math.max(1, Math.ceil(n / cols) - 1 || 1);
    locs.forEach((l, i) => {
        layout.set(l.id, { x: mx + (cols > 1 ? (i % cols) * cw : (MAP_W - 2 * mx) / 2), y: my + Math.floor(i / cols) * ch });
    });
    return layout;
}

// Realistic layout: force-directed from the road network (edge length follows real distance),
// computed once. Connected locations cluster; the city packs around its hubs, nature radiates out.
function graphLayout(locs, conns) {
    const ids = locs.map((l) => l.id);
    const n = ids.length || 1;
    const idx = new Map(ids.map((id, i) => [id, i]));
    const pos = ids.map((_, i) => ({ x: 400 + 250 * Math.cos((i / n) * 2 * Math.PI), y: 280 + 250 * Math.sin((i / n) * 2 * Math.PI) }));
    const edges = conns.filter((c) => idx.has(c.from) && idx.has(c.to)).map((c) => ({ a: idx.get(c.from), b: idx.get(c.to), d: c.dist }));
    const maxD = Math.max(1, ...edges.map((e) => e.d));
    const iters = 320;
    for (let it = 0; it < iters; it++) {
        const disp = pos.map(() => ({ x: 0, y: 0 }));
        for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) {
            let dx = pos[i].x - pos[j].x, dy = pos[i].y - pos[j].y; const dist = Math.hypot(dx, dy) || 0.01;
            const rep = 5000 / dist; dx /= dist; dy /= dist;
            disp[i].x += dx * rep; disp[i].y += dy * rep; disp[j].x -= dx * rep; disp[j].y -= dy * rep;
        }
        for (const e of edges) {
            let dx = pos[e.a].x - pos[e.b].x, dy = pos[e.a].y - pos[e.b].y; const dist = Math.hypot(dx, dy) || 0.01;
            const ideal = 45 + (e.d / maxD) * 170;
            const att = (dist - ideal) * 0.06; dx /= dist; dy /= dist;
            disp[e.a].x -= dx * att; disp[e.a].y -= dy * att; disp[e.b].x += dx * att; disp[e.b].y += dy * att;
        }
        const cool = 1 - it / iters;
        for (let i = 0; i < n; i++) { pos[i].x += disp[i].x * 0.08 * cool; pos[i].y += disp[i].y * 0.08 * cool; }
    }
    // Fit to the viewBox with margins.
    const xs = pos.map((p) => p.x), ys = pos.map((p) => p.y);
    const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
    const m = 40, sx = (MAP_W - 2 * m) / Math.max(1, maxX - minX), sy = (MAP_H - 2 * m) / Math.max(1, maxY - minY);
    const layout = new Map();
    ids.forEach((id, i) => layout.set(id, { x: m + (pos[i].x - minX) * sx, y: m + (pos[i].y - minY) * sy }));
    return layout;
}

// (Re)builds the static SVG layer (roads + location nodes) for the current mode + location set.
function buildMapStatic(svg, locs, conns, drawRoads) {
    clear(svg);
    mapDots.clear();
    if (drawRoads) {
        const roadLayer = svgEl("g", {});
        for (const c of conns) {
            const a = mapLayout.get(c.from), b = mapLayout.get(c.to);
            if (a && b) roadLayer.appendChild(svgEl("line", { class: "map-road", x1: a.x, y1: a.y, x2: b.x, y2: b.y }));
        }
        svg.appendChild(roadLayer);
    }
    const nodeLayer = svgEl("g", {});
    for (const l of locs) {
        const p = mapLayout.get(l.id); if (!p) continue;
        const region = (l.region || "").toLowerCase();
        nodeLayer.appendChild(svgEl("circle", { class: "map-node " + (region === "nature" ? "nature" : "city"), cx: p.x, cy: p.y, r: 3 }));
        const label = svgEl("text", { class: "map-node-label", x: p.x, y: p.y - 6, "text-anchor": "middle" });
        label.textContent = l.displayName.length > 14 ? l.displayName.slice(0, 13) + "…" : l.displayName;
        nodeLayer.appendChild(label);
    }
    svg.appendChild(nodeLayer);
    svg.appendChild(svgEl("g", { id: "map-dot-layer" }));
}

// Text fallback: locations and who is standing in each (from the dynamic occupied list).
function renderMapList(state) {
    const root = $("maplist");
    clear(root);
    for (const l of state.locations || []) {
        const box = el("div", { class: "loc" });
        box.appendChild(el("div", { class: "loc-name", text: l.displayName }));
        if (l.characterIds.length) {
            for (const id of l.characterIds) {
                box.appendChild(el("div", { class: isDead(id) ? "occupant dead" : "occupant", text: "• " + (nameById.get(id) || id.slice(0, 8)) }));
            }
        } else { box.appendChild(el("div", { class: "empty", text: "prázdno" })); }
        root.appendChild(box);
    }
}

function renderMap(state) {
    const svg = $("mapsvg"), list = $("maplist");
    if (mapMode === "list") {
        svg.hidden = true; list.hidden = false; renderMapList(state); return;
    }
    svg.hidden = false; list.hidden = true;

    const locs = (state.mapLocations || []).slice().sort((a, b) => a.id.localeCompare(b.id));
    const conns = state.mapConnections || [];
    const staticKey = mapMode + "|" + locs.map((l) => l.id).join(",");
    if (staticKey !== mapStaticKey) {
        mapStaticKey = staticKey;
        mapLayout = mapMode === "graph" ? graphLayout(locs, conns) : gridLayout(locs);
        buildMapStatic(svg, locs, conns, mapMode === "graph");
    }
    const dotLayer = svg.querySelector("#map-dot-layer");
    if (!dotLayer) return;

    // Cluster co-located characters so their dots don't fully overlap.
    const byLoc = new Map();
    for (const c of state.characters) {
        const arr = byLoc.get(c.location) || []; arr.push(c.id); byLoc.set(c.location, arr);
    }

    const seen = new Set();
    for (const c of state.characters) {
        seen.add(c.id);
        let e = mapDots.get(c.id);
        if (!e) {
            const g = svgEl("g", { class: "map-dot" });
            const dot = svgEl("circle", { r: 5 });
            const title = svgEl("title", {});
            g.appendChild(dot); g.appendChild(title);
            g.addEventListener("click", () => selectNode(c.id));
            dotLayer.appendChild(g);
            e = { g, dot, title };
            mapDots.set(c.id, e);
        }
        e.dot.setAttribute("class", c.status === "Dead" ? "dead" : (c.sex === "Female" ? "female" : "male"));
        e.g.classList.toggle("selected", c.id === selectedId);
        e.title.textContent = `${c.name} ${c.surname || ""}`.trim();

        // In transit → interpolate ALONG the road (origin→destination by progress). Otherwise sit at
        // the location node (+ small cluster offset). Unknown position → keep last (or center first time).
        const from = c.travelFromId && mapLayout.get(c.travelFromId);
        const to = c.travelToId && mapLayout.get(c.travelToId);
        if (c.travelProgress != null && from && to) {
            const t = c.travelProgress;
            e.g.setAttribute("transform", `translate(${(from.x + (to.x - from.x) * t).toFixed(1)},${(from.y + (to.y - from.y) * t).toFixed(1)})`);
        } else {
            const pos = mapLayout.get(c.location);
            if (pos) {
                const peers = byLoc.get(c.location) || [c.id];
                const i = peers.indexOf(c.id);
                const ring = peers.length > 1 ? 9 : 0;
                const ang = peers.length > 1 ? (i / peers.length) * Math.PI * 2 : 0;
                e.g.setAttribute("transform", `translate(${(pos.x + Math.cos(ang) * ring).toFixed(1)},${(pos.y + Math.sin(ang) * ring).toFixed(1)})`);
            } else if (!e.g.hasAttribute("transform")) {
                e.g.setAttribute("transform", `translate(${MAP_W / 2},${MAP_H / 2})`);
            }
        }
    }

    for (const [id, e] of mapDots) {
        if (!seen.has(id)) { e.g.remove(); mapDots.delete(id); }
    }
}

// Mode switch (persisted). Reset the static key so the next render rebuilds in the new mode.
(function wireMapMode() {
    const sel = $("mapMode");
    sel.value = mapMode;
    sel.addEventListener("change", () => {
        mapMode = sel.value;
        localStorage.setItem("wo.mapMode", mapMode);
        mapStaticKey = "";
        if (lastState) renderMap(lastState);
    });
})();

// ── Relationship graph ──────────────────────────────────────────────────────────
function renderGraph() {
    const { characters, edges } = last;
    const svg = $("graph");
    clear(svg);
    const cx = 200, cy = 200, r = 150;
    const n = characters.length || 1;
    const pos = new Map();
    characters.forEach((c, i) => {
        const a = (i / n) * Math.PI * 2 - Math.PI / 2;
        pos.set(c.id, { x: cx + r * Math.cos(a), y: cy + r * Math.sin(a) });
    });

    // Classify each edge so partner / strong bonds read at a glance.
    //  tier 2 = partner (KinRole), 1 = strong (high closeness/commitment), 0 = ordinary.
    const tierOf = (e) => {
        if (e.kinRole === "Partner") return 2;
        if (e.closeness >= 60 || e.commitment >= 50) return 1;
        return 0;
    };
    // Draw ordinary first, strong, then partner on top so the important bonds aren't buried.
    const ordered = edges.slice().sort((a, b) => tierOf(a) - tierOf(b));
    const hearts = [];
    const trendMarks = []; // predicted direction markers for strongly-moving bonds

    for (const e of ordered) {
        const p = pos.get(e.from), q = pos.get(e.to);
        if (!p || !q) continue;
        const dead = isDead(e.from) || isDead(e.to);
        const tier = tierOf(e);
        const baseW = 0.5 + (e.closeness / 100) * 4;
        let cls = "edge-line";
        let width = baseW;
        let opacity = 0.15 + (e.closeness / 100) * 0.75;
        if (dead) {
            cls += " dead"; opacity = 0.25;
        } else if (tier === 2) {
            cls += " partner"; width = Math.max(3.5, baseW); opacity = 0.95;
        } else if (tier === 1) {
            cls += " strong"; width = Math.max(2.2, baseW); opacity = Math.max(0.7, opacity);
        }
        svg.appendChild(svgEl("line", {
            class: cls,
            x1: p.x, y1: p.y, x2: q.x, y2: q.y,
            "stroke-width": width,
            "stroke-opacity": opacity,
        }));

        // One heart per partner pair (dedupe the reciprocal edge), placed at the line midpoint.
        if (!dead && tier === 2 && e.from < e.to) {
            hearts.push({ x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 });
        }

        // Predicted direction: mark strongly deepening (▲) or cooling (▼) bonds.
        if (!dead && e.from < e.to) {
            const tr = relTrend(edgeKey(e));
            if (tr.dir !== "flat" && Math.abs(tr.slope) >= 0.4) {
                trendMarks.push({
                    x: p.x + (q.x - p.x) * 0.38,
                    y: p.y + (q.y - p.y) * 0.38,
                    up: tr.dir === "up",
                });
            }
        }
    }

    for (const h of hearts) {
        const heart = svgEl("text", { class: "edge-heart", x: h.x, y: h.y });
        heart.textContent = "♥";
        svg.appendChild(heart);
    }

    for (const m of trendMarks) {
        const mark = svgEl("text", { class: "edge-trend " + (m.up ? "up" : "down"), x: m.x, y: m.y });
        mark.textContent = m.up ? "▲" : "▼";
        svg.appendChild(mark);
    }

    for (const c of characters) {
        const p = pos.get(c.id);
        const dead = c.status === "Dead";
        let dotClass = "node-dot";
        if (dead) dotClass += " dead";
        if (c.id === selectedId) dotClass += " selected";

        const dot = svgEl("circle", { class: dotClass, cx: p.x, cy: p.y, r: 7 });
        dot.addEventListener("click", () => selectNode(c.id));
        svg.appendChild(dot);

        const left = p.x < cx;
        const label = svgEl("text", {
            class: dead ? "node-label dead" : "node-label",
            x: p.x + (left ? -10 : 10),
            y: p.y + 4,
            "text-anchor": left ? "end" : "start",
        });
        label.textContent = dead ? c.name + " ✝" : c.name;
        label.addEventListener("click", () => selectNode(c.id));
        svg.appendChild(label);
    }
}

function selectNode(id) {
    selectedId = (selectedId === id) ? null : id; // click again to deselect
    history = [];
    histId = selectedId;
    if (selectedId) {
        const cur = last.characters.find((x) => x.id === selectedId);
        if (cur) history.push(cur);
    }
    renderVitals(last.characters);
    renderGraph();
    renderDetail();
}

// ── Relationship detail (evolution) ─────────────────────────────────────────────
const DIMENSIONS = [
    ["Sympatie", "like"],
    ["Důvěra", "trust"],
    ["Blízkost", "closeness"],
    ["Respekt", "respect"],
    ["Pohodlí", "comfort"],
    ["Známost", "familiarity"],
    ["Komunální síla", "communalStrength"],
    ["Směnná síla", "exchangeStrength"],
    ["Intimita", "intimateAffinity"],
    ["Sexuální zájem", "sexualInterest"],
    ["Estetická přitažlivost", "aestheticAttraction"],
    ["Fyzická přitažlivost", "physicalAttraction"],
    ["Responzivní touha", "responsiveDesire"],
    ["Závazek", "commitment"],
    ["Investice", "investmentSize"],
    ["Kvalita alternativ", "alternativeQuality"],
    ["Reziduum prohřešků", "transgressionResidue"],
    ["Vnímaná dominance", "perceivedDominance"],
    ["Vnímaná prestiž", "perceivedPrestige"],
];

// Czech label for a kinship role (KinRole enum).
function kinLabel(k) {
    switch (k) {
        case "Partner": return "partner";
        case "Parent": return "rodič";
        case "Child": return "dítě";
        case "Sibling": return "sourozenec";
        case "Grandparent": return "prarodič";
        case "Grandchild": return "vnouče";
        default: return null;
    }
}

function trendSpan(val, prevVal) {
    let arrow = "·", cls = "trend-flat";
    if (prevVal !== undefined && prevVal !== null) {
        const d = val - prevVal;
        if (d > 0.05) { arrow = "▲"; cls = "trend-up"; }
        else if (d < -0.05) { arrow = "▼"; cls = "trend-down"; }
    }
    return el("span", { class: cls, text: arrow });
}

function dimRow(label, val, prevVal, decimals = 1) {
    const row = el("div", { class: "rel-dim" });
    row.appendChild(el("span", { class: "k", text: label }));
    const v = el("span", { class: "v" });
    v.appendChild(document.createTextNode(val.toFixed(decimals) + " "));
    v.appendChild(trendSpan(val, prevVal));
    row.appendChild(v);
    return row;
}

function section(title, rows) {
    const box = el("div", { class: "detail-section" });
    box.appendChild(el("h4", { text: title }));
    const grid = el("div", { class: "rel-dims" });
    for (const r of rows) grid.appendChild(r);
    box.appendChild(grid);
    return box;
}

// Build trend rows from [label, accessor, decimals, key] specs. Rows whose metric `key` is
// currently charted are skipped (shown as a chart instead), and missing values are skipped.
function accRows(c, p, specs) {
    const rows = [];
    for (const [label, acc, dec, key] of specs) {
        if (key && selectedKeys.has(key)) continue;
        const v = acc(c);
        if (v === undefined || v === null) continue;
        rows.push(dimRow(label, v, acc(p), dec ?? 1));
    }
    return rows;
}
function appendSection(box, title, c, p, specs) {
    const rows = accRows(c, p, specs);
    if (rows.length) box.appendChild(section(title, rows));
}

// ── Live sparkline charts ───────────────────────────────────────────────────────
// Full catalogue of chartable metrics; the user picks which to draw (persisted).
const METRIC_CATALOG = [
    // Psychika
    { k: "psy.valence", g: "Psychika", label: "Valence", get: (c) => c?.valence, min: -1, max: 1 },
    { k: "psy.arousal", g: "Psychika", label: "Arousal", get: (c) => c?.arousal, min: 0, max: 1 },
    { k: "psy.dominance", g: "Psychika", label: "Dominance", get: (c) => c?.dominance, min: 0, max: 1 },
    { k: "psy.stress", g: "Psychika", label: "Stres", get: (c) => c?.stress, min: 0, max: 100 },
    { k: "psy.mood", g: "Psychika", label: "Nálada", get: (c) => c?.moodBaseline, min: 0, max: 100 },
    { k: "psy.cog", g: "Psychika", label: "Kogn. zátěž", get: (c) => c?.cognitiveLoad, min: 0, max: 100 },
    { k: "psy.nSocial", g: "Psychika", label: "Potřeba sociální", get: (c) => c?.needSocial, min: 0, max: 100 },
    { k: "psy.nIntim", g: "Psychika", label: "Potřeba intimita", get: (c) => c?.needIntimacy, min: 0, max: 100 },
    { k: "psy.nAch", g: "Psychika", label: "Potřeba výkon", get: (c) => c?.needAchievement, min: 0, max: 100 },
    { k: "psy.nCare", g: "Psychika", label: "Potřeba péče", get: (c) => c?.needCare, min: 0, max: 100 },
    { k: "psy.nSafe", g: "Psychika", label: "Potřeba bezpečí", get: (c) => c?.needSafety, min: 0, max: 100 },
    // Fyziologie
    { k: "phys.energy", g: "Fyziologie", label: "Energie", get: (c) => c?.energy, min: 0, max: 100 },
    { k: "phys.hunger", g: "Fyziologie", label: "Hlad", get: (c) => c?.hunger, min: 0, max: 100 },
    { k: "phys.thirst", g: "Fyziologie", label: "Žízeň", get: (c) => c?.thirst, min: 0, max: 100 },
    { k: "phys.pain", g: "Fyziologie", label: "Bolest", get: (c) => c?.pain, min: 0, max: 100 },
    { k: "phys.immune", g: "Fyziologie", label: "Imunita", get: (c) => c?.immuneLoad, min: 0, max: 100 },
    { k: "phys.cortisol", g: "Fyziologie", label: "Kortizol", get: (c) => c?.cortisol, min: 0, max: 100 },
    { k: "phys.allostatic", g: "Fyziologie", label: "Alost. zátěž", get: (c) => c?.allostaticLoad, min: 0, max: 100 },
    { k: "phys.fatigue", g: "Fyziologie", label: "Fyz. únava", get: (c) => c?.physicalFatigue, min: 0, max: 100 },
    { k: "phys.sleepDebt", g: "Fyziologie", label: "Spánkový dluh", get: (c) => c?.sleepDebtHours },
    { k: "phys.temp", g: "Fyziologie", label: "Teplota Δ", get: (c) => c?.bodyTempDelta },
    { k: "phys.sam", g: "Fyziologie", label: "Akutní arousal", get: (c) => c?.physio?.acuteArousal, min: 0, max: 100 },
    { k: "phys.processS", g: "Fyziologie", label: "Spánkový tlak", get: (c) => c?.physio?.processS, min: 0, max: 1 },
    // Pudy
    { k: "drv.rest", g: "Pudy", label: "Odpočinek", get: (c) => c?.drives?.rest, min: 0, max: 100 },
    { k: "drv.food", g: "Pudy", label: "Jídlo", get: (c) => c?.drives?.food, min: 0, max: 100 },
    { k: "drv.water", g: "Pudy", label: "Voda", get: (c) => c?.drives?.water, min: 0, max: 100 },
    { k: "drv.belong", g: "Pudy", label: "Sounáležitost", get: (c) => c?.drives?.belonging, min: 0, max: 100 },
    { k: "drv.comp", g: "Pudy", label: "Kompetence", get: (c) => c?.drives?.competence, min: 0, max: 100 },
    { k: "drv.intim", g: "Pudy", label: "Intimita", get: (c) => c?.drives?.intimacy, min: 0, max: 100 },
    // Hodnoty (Schwartz)
    { k: "val.benev", g: "Hodnoty", label: "Benevolence", get: (c) => c?.values?.benevolence, min: 0, max: 1 },
    { k: "val.univ", g: "Hodnoty", label: "Univerzalismus", get: (c) => c?.values?.universalism, min: 0, max: 1 },
    { k: "val.selfdir", g: "Hodnoty", label: "Samostatnost", get: (c) => c?.values?.selfDirection, min: 0, max: 1 },
    { k: "val.stim", g: "Hodnoty", label: "Stimulace", get: (c) => c?.values?.stimulation, min: 0, max: 1 },
    { k: "val.hedon", g: "Hodnoty", label: "Hédonismus", get: (c) => c?.values?.hedonism, min: 0, max: 1 },
    { k: "val.achiev", g: "Hodnoty", label: "Úspěch", get: (c) => c?.values?.achievement, min: 0, max: 1 },
    { k: "val.power", g: "Hodnoty", label: "Moc", get: (c) => c?.values?.power, min: 0, max: 1 },
    { k: "val.secur", g: "Hodnoty", label: "Bezpečí", get: (c) => c?.values?.security, min: 0, max: 1 },
    { k: "val.conf", g: "Hodnoty", label: "Konformita", get: (c) => c?.values?.conformity, min: 0, max: 1 },
    { k: "val.trad", g: "Hodnoty", label: "Tradice", get: (c) => c?.values?.tradition, min: 0, max: 1 },
    // Zájmy (RIASEC)
    { k: "int.real", g: "Zájmy", label: "Realistické", get: (c) => c?.interests?.realistic, min: 0, max: 1 },
    { k: "int.inv", g: "Zájmy", label: "Investigativní", get: (c) => c?.interests?.investigative, min: 0, max: 1 },
    { k: "int.art", g: "Zájmy", label: "Umělecké", get: (c) => c?.interests?.artistic, min: 0, max: 1 },
    { k: "int.soc", g: "Zájmy", label: "Sociální", get: (c) => c?.interests?.social, min: 0, max: 1 },
    { k: "int.ent", g: "Zájmy", label: "Podnikavé", get: (c) => c?.interests?.enterprising, min: 0, max: 1 },
    { k: "int.conv", g: "Zájmy", label: "Konvenční", get: (c) => c?.interests?.conventional, min: 0, max: 1 },
    // Sebepojetí
    { k: "self.esteem", g: "Sebepojetí", label: "Sebevědomí", get: (c) => c?.self?.selfEsteem, min: 0, max: 1 },
    { k: "self.discr", g: "Sebepojetí", label: "Sebe-diskrepance", get: (c) => c?.self?.selfDiscrepancy, min: 0, max: 1 },
    // Biologie (cykly) — accessors are null-safe; absent for characters without the sub-state
    { k: "bio.testo", g: "Biologie", label: "Testosteron", get: (c) => c?.bio?.testosterone, min: 0, max: 100 },
    { k: "bio.estradiol", g: "Biologie", label: "Estradiol", get: (c) => c?.bio?.cycle?.estradiol, min: 0, max: 100 },
    { k: "bio.progest", g: "Biologie", label: "Progesteron", get: (c) => c?.bio?.cycle?.progesterone, min: 0, max: 100 },
    { k: "bio.cycPain", g: "Biologie", label: "Cyklus: bolest", get: (c) => c?.bio?.cycle?.symptomPain, min: 0, max: 100 },
    { k: "bio.cycBreast", g: "Biologie", label: "Cyklus: citlivost prsou", get: (c) => c?.bio?.cycle?.symptomBreastTender, min: 0, max: 100 },
    { k: "bio.cycBloat", g: "Biologie", label: "Cyklus: nadýmání", get: (c) => c?.bio?.cycle?.symptomBloat, min: 0, max: 100 },
    { k: "bio.cycLibido", g: "Biologie", label: "Cyklus: libido ×", get: (c) => c?.bio?.cycle?.libidoMod, min: 0.5, max: 1.5 },
    { k: "bio.nutCal", g: "Biologie", label: "Nutrice: kalorie", get: (c) => c?.bio?.nutrition?.calories, min: 0, max: 100 },
    { k: "bio.nutVitD", g: "Biologie", label: "Nutrice: vitamin D", get: (c) => c?.bio?.nutrition?.vitaminD, min: 0, max: 100 },
    { k: "bio.nutIron", g: "Biologie", label: "Nutrice: železo", get: (c) => c?.bio?.nutrition?.iron, min: 0, max: 100 },
    { k: "bio.nutProtein", g: "Biologie", label: "Nutrice: bílkoviny", get: (c) => c?.bio?.nutrition?.protein, min: 0, max: 100 },
    { k: "bio.nutGlucose", g: "Biologie", label: "Nutrice: glukóza", get: (c) => c?.bio?.nutrition?.bloodGlucose, min: 0, max: 100 },
];

const DEFAULT_METRICS = [
    "psy.valence", "psy.arousal", "psy.dominance", "psy.stress", "psy.mood", "psy.cog",
    "phys.energy", "phys.hunger", "phys.thirst", "phys.cortisol", "phys.allostatic", "phys.immune",
];

function loadSelectedKeys() {
    try {
        const s = JSON.parse(localStorage.getItem("wo.chartMetrics"));
        if (Array.isArray(s) && s.length) return new Set(s);
    } catch (e) { /* ignore */ }
    return new Set(DEFAULT_METRICS);
}
function saveSelectedKeys() {
    try { localStorage.setItem("wo.chartMetrics", JSON.stringify([...selectedKeys])); } catch (e) { /* ignore */ }
}
let selectedKeys = loadSelectedKeys();

function sparkline(label, series, lo, hi) {
    const wrap = el("div", { class: "spark" });
    const head = el("div", { class: "spark-head" });
    head.appendChild(el("span", { class: "spark-label", text: label }));
    const cur = series.length ? series[series.length - 1] : null;
    head.appendChild(el("span", { class: "spark-val", text: cur == null ? "—" : cur.toFixed(2) }));
    wrap.appendChild(head);

    const W = 100, H = 30;
    const svg = svgEl("svg", { class: "spark-svg", viewBox: `0 0 ${W} ${H}`, preserveAspectRatio: "none" });
    if (series.length >= 2) {
        let mn = lo, mx = hi;
        if (mn === undefined || mx === undefined) { mn = Math.min(...series); mx = Math.max(...series); }
        if (mx - mn < 1e-9) mx = mn + 1;
        const n = series.length;
        const pts = series.map((v, i) => {
            const x = (i / (n - 1)) * W;
            const y = H - ((v - mn) / (mx - mn)) * H;
            return `${x.toFixed(1)},${y.toFixed(1)}`;
        }).join(" ");
        svg.appendChild(svgEl("polyline", { class: "spark-line", points: pts, fill: "none" }));
    }
    wrap.appendChild(svg);
    return wrap;
}

function renderCharts() {
    const box = el("div", { class: "detail-section" });
    box.appendChild(el("h4", { text: `Vývoj v čase (${history.length} vzorků)` }));
    const grid = el("div", { class: "chart-grid" });
    const chosen = METRIC_CATALOG.filter((m) => selectedKeys.has(m.k));
    if (!chosen.length) {
        grid.appendChild(el("div", { class: "rel-detail-hint", text: "Žádná metrika nevybraná — viz ⚙ Metriky grafů." }));
    }
    for (const m of chosen) {
        const series = history.map(m.get).filter((v) => typeof v === "number");
        grid.appendChild(sparkline(m.label, series, m.min, m.max));
    }
    box.appendChild(grid);
    return box;
}

// Named metric presets, persisted in localStorage as { name: [keys] }.
function loadPresets() {
    try { return JSON.parse(localStorage.getItem("wo.chartPresets")) || {}; } catch (e) { return {}; }
}
function savePresets(p) {
    try { localStorage.setItem("wo.chartPresets", JSON.stringify(p)); } catch (e) { /* ignore */ }
}
function applySelection(keys) {
    selectedKeys = new Set(keys);
    saveSelectedKeys();
    buildMetricPicker();
    renderDetail();
}

// Static metrics picker (built once + on explicit actions; not wiped by the per-tick re-render).
function buildMetricPicker() {
    const body = document.getElementById("metric-picker-body");
    if (!body) return;
    clear(body);

    // Toolbar: select all / clear all / save preset
    const tb = el("div", { class: "mp-toolbar" });
    const allBtn = el("button", { class: "mp-btn", text: "Vše" });
    allBtn.type = "button";
    allBtn.addEventListener("click", () => applySelection(METRIC_CATALOG.map((m) => m.k)));
    const noneBtn = el("button", { class: "mp-btn", text: "Nic" });
    noneBtn.type = "button";
    noneBtn.addEventListener("click", () => applySelection([]));
    const nameInp = document.createElement("input");
    nameInp.type = "text";
    nameInp.placeholder = "název presetu";
    nameInp.className = "mp-name";
    const saveBtn = el("button", { class: "mp-btn", text: "Uložit preset" });
    saveBtn.type = "button";
    saveBtn.addEventListener("click", () => {
        const name = nameInp.value.trim();
        if (!name) return;
        const p = loadPresets();
        p[name] = [...selectedKeys];
        savePresets(p);
        nameInp.value = "";
        buildMetricPicker();
    });
    tb.appendChild(allBtn);
    tb.appendChild(noneBtn);
    tb.appendChild(nameInp);
    tb.appendChild(saveBtn);
    body.appendChild(tb);

    // Saved presets (apply / delete)
    const presets = loadPresets();
    const names = Object.keys(presets);
    if (names.length) {
        const pr = el("div", { class: "mp-presets" });
        for (const name of names) {
            const chip = el("span", { class: "mp-preset" });
            const apply = el("button", { class: "mp-preset-apply", text: name });
            apply.type = "button";
            apply.title = "použít preset";
            apply.addEventListener("click", () => applySelection(presets[name]));
            const del = el("button", { class: "mp-preset-del", text: "×" });
            del.type = "button";
            del.title = "smazat preset";
            del.addEventListener("click", () => {
                const p = loadPresets();
                delete p[name];
                savePresets(p);
                buildMetricPicker();
            });
            chip.appendChild(apply);
            chip.appendChild(del);
            pr.appendChild(chip);
        }
        body.appendChild(pr);
    }

    // Grouped checkboxes
    const groupsWrap = el("div", { class: "mp-groups" });
    const groups = new Map();
    for (const m of METRIC_CATALOG) {
        if (!groups.has(m.g)) groups.set(m.g, []);
        groups.get(m.g).push(m);
    }
    for (const [g, items] of groups) {
        const gd = el("div", { class: "mp-group" });
        gd.appendChild(el("div", { class: "mp-group-title", text: g }));
        const wrap = el("div", { class: "mp-items" });
        for (const m of items) {
            const lab = el("label", { class: "mp-item" });
            const cb = document.createElement("input");
            cb.type = "checkbox";
            cb.checked = selectedKeys.has(m.k);
            cb.addEventListener("change", () => {
                if (cb.checked) selectedKeys.add(m.k); else selectedKeys.delete(m.k);
                saveSelectedKeys();
                renderDetail();
            });
            lab.appendChild(cb);
            lab.appendChild(document.createTextNode(" " + m.label));
            wrap.appendChild(lab);
        }
        gd.appendChild(wrap);
        groupsWrap.appendChild(gd);
    }
    body.appendChild(groupsWrap);
}

function renderDetail() {
    const box = $("rel-detail");
    clear(box);

    if (!selectedId) {
        box.appendChild(el("div", { class: "rel-detail-hint", text: "Klikni na postavu (v grafu nebo ve Vitálech) a uvidíš její pohyb, fyziologii, psychiku a vývoj vztahů." }));
        return;
    }

    const c = last.characters.find((x) => x.id === selectedId);
    const p = prevCharById.get(selectedId);

    const h = el("h3", { text: (c ? c.name : nameById.get(selectedId) || selectedId.slice(0, 8)) });
    if (isDead(selectedId)) h.appendChild(el("span", { class: "dead-badge", text: "✝ mrtvá" }));
    const detailPartner = partnerNameOf(selectedId);
    if (detailPartner) h.appendChild(el("span", { class: "partner-badge", text: "♥ partner: " + detailPartner }));
    box.appendChild(h);

    if (isDead(selectedId) && c?.deathCause) {
        box.appendChild(el("div", { class: "death-cause", text: "☠ Důvod úmrtí: " + c.deathCause }));
    }

    if (c) {
        // Live charts of selected metrics
        box.appendChild(renderCharts());

        // Chosen interaction this tick
        if (c.interaction) {
            const tgt = c.interaction.targetId ? (nameById.get(c.interaction.targetId) || c.interaction.targetId.slice(0, 8)) : "—";
            const iRows = [row("Akt", c.interaction.act), row("S kým", tgt)];
            if (c.interaction.content) {
                const cr = el("div", { class: "rel-dim wide" });
                cr.appendChild(el("span", { class: "k", text: "Obsah" }));
                cr.appendChild(el("span", { class: "v", text: c.interaction.content }));
                iRows.push(cr);
            }
            box.appendChild(section("Zvolená interakce", iRows));
        }

        // Movement
        const trailNames = (c.trail || []).map((id) => locNameById.get(id) || id);
        const trailRow = el("div", { class: "rel-dim wide" });
        trailRow.appendChild(el("span", { class: "k", text: "Stopa" }));
        trailRow.appendChild(el("span", { class: "v", text: trailNames.length ? trailNames.join(" → ") : "—" }));
        box.appendChild(section("Pohyb", [
            row("Lokace", c.location),
            row("Akce", c.currentAction || "—"),
            row("Na cestě do", c.travelingTo || "—"),
            row("Povolání", c.occupation || "—"),
            trailRow,
        ]));

        // Physiology (full) — metrics already drawn as charts are omitted here (4th item = metric key).
        appendSection(box, "Fyziologie", c, p, [
            ["Energie", (x) => x?.energy, 1, "phys.energy"], ["Hlad", (x) => x?.hunger, 1, "phys.hunger"], ["Žízeň", (x) => x?.thirst, 1, "phys.thirst"],
            ["Bolest", (x) => x?.pain, 1, "phys.pain"], ["Spánkový dluh (h)", (x) => x?.sleepDebtHours, 1, "phys.sleepDebt"], ["Imunita", (x) => x?.immuneLoad, 1, "phys.immune"],
            ["Kortizol", (x) => x?.cortisol, 1, "phys.cortisol"], ["Alostatická zátěž", (x) => x?.allostaticLoad, 1, "phys.allostatic"], ["Fyz. únava", (x) => x?.physicalFatigue, 1, "phys.fatigue"],
            ["Tělesná teplota Δ°C", (x) => x?.bodyTempDelta, 2, "phys.temp"], ["Akutní arousal (SAM)", (x) => x?.physio?.acuteArousal, 1, "phys.sam"],
            ["Zotavovací dluh (h)", (x) => x?.physio?.recoveryDebtHours], ["Spánková setrvačnost (h)", (x) => x?.physio?.sleepInertiaHours, 2],
            ["Chronická bolest (dny)", (x) => x?.physio?.chronicPainDays], ["Cirkadiánní posun (h)", (x) => x?.physio?.circadianPhaseShiftHours, 2],
            ["Spánkový tlak (Process S)", (x) => x?.physio?.processS, 3, "phys.processS"],
        ]);

        // Psychology (full)
        const psyRows = accRows(c, p, [
            ["Valence", (x) => x?.valence, 2, "psy.valence"], ["Arousal", (x) => x?.arousal, 2, "psy.arousal"], ["Dominance", (x) => x?.dominance, 2, "psy.dominance"],
            ["Stres", (x) => x?.stress, 1, "psy.stress"], ["Nálada", (x) => x?.moodBaseline, 1, "psy.mood"], ["Kognitivní zátěž", (x) => x?.cognitiveLoad, 1, "psy.cog"],
            ["Potřeba: sociální", (x) => x?.needSocial, 1, "psy.nSocial"], ["Potřeba: intimita", (x) => x?.needIntimacy, 1, "psy.nIntim"],
            ["Potřeba: výkon", (x) => x?.needAchievement, 1, "psy.nAch"], ["Potřeba: péče", (x) => x?.needCare, 1, "psy.nCare"], ["Potřeba: bezpečí", (x) => x?.needSafety, 1, "psy.nSafe"],
        ]);
        psyRows.unshift(row("Emoce", c.emotion));
        if (c.sicknessWithdraw) psyRows.push(row("Nemocenské stažení", "ano"));
        box.appendChild(section("Psychika", psyRows));

        // Behavior drives
        appendSection(box, "Pudy (chování)", c, p, [
            ["Odpočinek", (x) => x?.drives?.rest, 1, "drv.rest"], ["Jídlo", (x) => x?.drives?.food, 1, "drv.food"], ["Voda", (x) => x?.drives?.water, 1, "drv.water"],
            ["Sounáležitost", (x) => x?.drives?.belonging, 1, "drv.belong"], ["Kompetence", (x) => x?.drives?.competence, 1, "drv.comp"], ["Intimita", (x) => x?.drives?.intimacy, 1, "drv.intim"],
        ]);

        // Biology (cycles) — only the sub-states that apply to this character
        const bio = c.bio || {};
        const bioRows = [];
        if (typeof bio.testosterone === "number") {
            bioRows.push(...accRows(c, p, [["Testosteron", (x) => x?.bio?.testosterone, 1, "bio.testo"]]));
        }
        if (bio.cycle) {
            bioRows.push(row("Fáze cyklu", bio.cycle.phase));
            bioRows.push(row("Den v cyklu", bio.cycle.dayInCycle));
            bioRows.push(row("Ovulační okno", bio.cycle.ovulationWindow ? "ano" : "ne"));
            bioRows.push(...accRows(c, p, [
                ["Estradiol", (x) => x?.bio?.cycle?.estradiol, 1, "bio.estradiol"],
                ["Progesteron", (x) => x?.bio?.cycle?.progesterone, 1, "bio.progest"],
                ["Bolest (cyklus)", (x) => x?.bio?.cycle?.symptomPain, 1, "bio.cycPain"],
                ["Citlivost prsou", (x) => x?.bio?.cycle?.symptomBreastTender, 1, "bio.cycBreast"],
                ["Nadýmání", (x) => x?.bio?.cycle?.symptomBloat, 1, "bio.cycBloat"],
                ["Libido (×)", (x) => x?.bio?.cycle?.libidoMod, 2, "bio.cycLibido"],
            ]));
            if (bio.cycle.pmddActive) bioRows.push(row("PMDD", "aktivní"));
            bioRows.push(row("Délka cyklu", bio.cycle.currentCycleLength));
        }
        if (bio.nutrition) {
            bioRows.push(...accRows(c, p, [
                ["Kalorie", (x) => x?.bio?.nutrition?.calories, 1, "bio.nutCal"],
                ["Vitamin D", (x) => x?.bio?.nutrition?.vitaminD, 1, "bio.nutVitD"],
                ["Železo", (x) => x?.bio?.nutrition?.iron, 1, "bio.nutIron"],
                ["Bílkoviny", (x) => x?.bio?.nutrition?.protein, 1, "bio.nutProtein"],
                ["Glukóza", (x) => x?.bio?.nutrition?.bloodGlucose, 1, "bio.nutGlucose"],
                ["Od jídla (h)", (x) => x?.bio?.nutrition?.postMealHours, 1],
            ]));
        }
        if (bioRows.length) box.appendChild(section("Biologie (cykly)", bioRows));

        // Reproduction
        const rr = c.reproduction || {};
        const reproRows = [row("Antikoncepce", rr.contraception || "—")];
        if (rr.fertileWindow) reproRows.push(row("Plodné okno", "ano"));
        if (rr.pregnant) {
            reproRows.push(row("Těhotná", `ano · ${rr.trimester}. trimestr`));
            reproRows.push(row("Dní těhotná", rr.daysPregnant));
            reproRows.push(row("Do porodu (dní)", rr.dueInDays));
            reproRows.push(row("Zjištěno", rr.discovered ? "ano" : "ne"));
            reproRows.push(row("Druhý rodič", rr.otherParent || "—"));
        }
        if (rr.postpartum) {
            reproRows.push(row("Po porodu", `${rr.postpartumPhase} · ${rr.postpartumDays} dní`));
        }
        box.appendChild(section("Reprodukce", reproRows));

        // Self-concept
        appendSection(box, "Sebepojetí", c, p, [
            ["Sebevědomí", (x) => x?.self?.selfEsteem, 3, "self.esteem"], ["Sebe-diskrepance", (x) => x?.self?.selfDiscrepancy, 3, "self.discr"],
            ["Vnímaná otevřenost", (x) => x?.self?.perceivedOpenness, 3], ["Vnímaná svědomitost", (x) => x?.self?.perceivedConscientiousness, 3],
            ["Vnímaná extraverze", (x) => x?.self?.perceivedExtraversion, 3], ["Vnímaná přívětivost", (x) => x?.self?.perceivedAgreeableness, 3],
            ["Vnímaný neuroticismus", (x) => x?.self?.perceivedNeuroticism, 3],
        ]);

        // Values (Schwartz)
        appendSection(box, "Hodnoty (Schwartz)", c, p, [
            ["Benevolence", (x) => x?.values?.benevolence, 3, "val.benev"], ["Univerzalismus", (x) => x?.values?.universalism, 3, "val.univ"],
            ["Samostatnost", (x) => x?.values?.selfDirection, 3, "val.selfdir"], ["Stimulace", (x) => x?.values?.stimulation, 3, "val.stim"],
            ["Hédonismus", (x) => x?.values?.hedonism, 3, "val.hedon"], ["Úspěch", (x) => x?.values?.achievement, 3, "val.achiev"],
            ["Moc", (x) => x?.values?.power, 3, "val.power"], ["Bezpečí", (x) => x?.values?.security, 3, "val.secur"],
            ["Konformita", (x) => x?.values?.conformity, 3, "val.conf"], ["Tradice", (x) => x?.values?.tradition, 3, "val.trad"],
        ]);

        // Interests (RIASEC)
        appendSection(box, "Zájmy (RIASEC)", c, p, [
            ["Realistické", (x) => x?.interests?.realistic, 3, "int.real"], ["Investigativní", (x) => x?.interests?.investigative, 3, "int.inv"],
            ["Umělecké", (x) => x?.interests?.artistic, 3, "int.art"], ["Sociální", (x) => x?.interests?.social, 3, "int.soc"],
            ["Podnikavé", (x) => x?.interests?.enterprising, 3, "int.ent"], ["Konvenční", (x) => x?.interests?.conventional, 3, "int.conv"],
        ]);
    }

    // Relationships — sorted by how developed the bond is (deepest first).
    const outgoing = last.edges
        .filter((e) => e.from === selectedId)
        .sort((a, b) => relDevelopment(b) - relDevelopment(a));
    const relBox = el("div", { class: "detail-section" });
    relBox.appendChild(el("h4", { text: "Vztahy (od nejrozvinutějšího)" }));
    if (!outgoing.length) {
        relBox.appendChild(el("div", { class: "rel-detail-hint", text: "Zatím žádné výrazné vztahy." }));
    } else {
        for (const e of outgoing) {
            const k = edgeKey(e);
            const prev = prevEdgeMap.get(k);
            const trend = relTrend(k);
            const forecast = relForecast(e, trend);
            const arrow = trend.dir === "up" ? "↗" : trend.dir === "down" ? "↘" : "→";
            const t = el("div", { class: "rel-target" });
            const nm = el("div", { class: "rel-name", text: "→ " + (nameById.get(e.to) || e.to.slice(0, 8)) });
            const kin = kinLabel(e.kinRole);
            if (kin) nm.appendChild(el("span", { class: "pic", text: " · " + kin }));
            nm.appendChild(el("span", { class: "pic " + (trend.dir === "up" ? "trend-up" : trend.dir === "down" ? "trend-down" : "trend-flat"), text: ` · rozvoj: ${Math.round(relDevelopment(e))} ${arrow}` }));
            nm.appendChild(el("span", { class: "pic", text: " · interakcí: " + e.positiveInteractions }));
            if (e.contemptuouslyDestroyed) nm.appendChild(el("span", { class: "pic flag-bad", text: " · pohrdání" }));
            if (e.dissolutionConsidered) nm.appendChild(el("span", { class: "pic flag-bad", text: " · zvažuje rozchod" }));
            t.appendChild(nm);

            // Prediction badge — inferred near-term outcome from Rusbult model + repair signals + trend.
            t.appendChild(el("div", { class: "rel-forecast " + forecast.cls, text: "🔮 " + forecast.text }));

            // Mutuality — compare this bond (A→B) with the reciprocal (B→A).
            const recip = lastEdgeMap.get(e.to + "|" + e.from);
            const myDev = Math.round(relDevelopment(e));
            if (!recip) {
                t.appendChild(el("div", { class: "rel-mutual fc-warn", text: `↔ jednostranný — neopětováno (ty ${myDev})` }));
            } else {
                const theirDev = Math.round(relDevelopment(recip));
                const diff = myDev - theirDev;
                let mu, cls;
                if (Math.abs(diff) <= 15) { mu = `vzájemný ≈ (ty ${myDev} / on ${theirDev})`; cls = "fc-good"; }
                else if (diff > 15) { mu = `jednostranný — investuješ víc (ty ${myDev} / on ${theirDev})`; cls = "fc-warn"; }
                else { mu = `jednostranný — druhý víc (ty ${myDev} / on ${theirDev})`; cls = "fc-warn"; }
                t.appendChild(el("div", { class: "rel-mutual " + cls, text: "↔ " + mu }));
            }

            const dims = el("div", { class: "rel-dims" });
            for (const [label, key] of DIMENSIONS) {
                dims.appendChild(dimRow(label, e[key], prev ? prev[key] : undefined));
            }
            t.appendChild(dims);

            // Development trajectory over recent ticks (auto-scaled).
            const hist = relHist.get(k);
            if (hist && hist.length >= 2) t.appendChild(sparkline("Vývoj vztahu", hist, undefined, undefined));

            relBox.appendChild(t);
        }
    }
    box.appendChild(relBox);
}

// A plain label/value row (no trend) for textual fields.
function row(label, value) {
    const r = el("div", { class: "rel-dim" });
    r.appendChild(el("span", { class: "k", text: label }));
    r.appendChild(el("span", { class: "v", text: String(value) }));
    return r;
}

// ── Incoming messages ──────────────────────────────────────────────────────────
let speedSynced = false;
connection.on("Tick", (state) => {
    $("clock").textContent = state.time;
    if (typeof state.elapsed === "string") $("elapsed").textContent = "⏱ " + state.elapsed;
    if (typeof state.realElapsed === "string") $("realElapsed").textContent = "🕒 " + state.realElapsed;
    reflectPaused(state.paused);

    // Reflect the server's configured tempo in the sliders, once.
    if (!speedSynced && typeof state.delayMs === "number") {
        speed.value = state.delayMs;
        $("speedVal").textContent = state.delayMs + " ms";
        if (typeof state.tickMinutes === "number") {
            tickmin.value = state.tickMinutes;
            $("tickminVal").textContent = state.tickMinutes + " min/tik";
        }
        speedSynced = true;
    }

    const statusById = new Map(state.characters.map((c) => [c.id, c.status]));
    for (const c of state.characters) nameById.set(c.id, c.name);
    for (const l of state.locations) locNameById.set(l.id, l.displayName);

    // Shift trend history: previous ← last current, then capture new current.
    prevEdgeMap = lastEdgeMap;
    lastEdgeMap = new Map(state.edges.map((e) => [edgeKey(e), e]));

    // Accumulate each edge's development trajectory (for trend + forecast in the detail panel).
    for (const e of state.edges) {
        const k = edgeKey(e);
        let h = relHist.get(k);
        if (!h) { h = []; relHist.set(k, h); }
        h.push(relDevelopment(e));
        if (h.length > REL_HIST_CAP) h.shift();
    }
    prevCharById = lastCharById;
    lastCharById = new Map(state.characters.map((c) => [c.id, c]));
    last = { characters: state.characters, edges: state.edges, statusById };

    // Accumulate the selected character's history for the charts.
    if (selectedId) {
        if (histId !== selectedId) { history = []; histId = selectedId; }
        const cur = lastCharById.get(selectedId);
        if (cur) {
            history.push(cur);
            if (history.length > HISTORY_MAX) history.shift();
        }
    }

    const total = state.characters.length;
    const alive = state.characters.filter((c) => c.status !== "Dead").length;
    $("popcount").textContent = `👥 ${total} (živých ${alive})`;

    lastState = state;
    renderVitals(state.characters);
    renderMap(state);
    renderGraph();
    renderDetail();
});

const feed = $("feed");
connection.on("Narrative", (entry) => {
    const li = el("li", { class: entry.priority });
    li.appendChild(el("span", { class: "t", text: entry.time }));
    li.appendChild(document.createTextNode(entry.text));
    feed.prepend(li);
    while (feed.childElementCount > 200) feed.removeChild(feed.lastChild);
});

// ── Connection lifecycle ───────────────────────────────────────────────────────
connection.onreconnecting(() => { conn.textContent = "připojuji…"; });
connection.onreconnected(() => { conn.textContent = "připojeno"; });
connection.onclose(() => { conn.textContent = "odpojeno"; });

buildMetricPicker();

connection.start()
    .then(() => { conn.textContent = "připojeno"; })
    .catch((err) => { conn.textContent = "chyba spojení"; console.error(err); });
