// Entry point (pure WASM, no Blazor): loads the .NET runtime, wires the DrawnUi.Web callbacks, runs the game.

import { dotnet } from './_framework/dotnet.js';
import { setModuleExports, showError } from './_content/DrawnUi.Web/drawnui-web.js';

// The [JSExport] Main anywhere in an exports tree.
function findExport(obj, name) {
    for (const key in obj) {
        const val = obj[key];
        if (key === name && typeof val === 'function') return val;
        if (val && typeof val === 'object') {
            const found = findExport(val, name);
            if (found) return found;
        }
    }
    return null;
}

// drawnui-web.js finds the Emscripten GL module through it.
globalThis.dotnet = dotnet;

// The page's loader (index.html) shows the real download progress and fades out when the game starts.
const loader = document.getElementById('loader');
let shown = 0; // the runtime adds files to the total as it goes: the bar never moves back
function onProgress(loaded, total) {
    if (!total) return;
    const pct = Math.max(shown, Math.min(100, Math.round(loaded * 100 / total)));
    if (pct === shown) return;
    shown = pct;
    document.documentElement.style.setProperty('--load-pct', pct + '%');
    document.documentElement.style.setProperty('--load-pct-text', `"${pct}%"`);
}
function hidePageLoader() {
    loader.classList.add('done');
    setTimeout(() => loader.remove(), 400);
}

try {
    const { getAssemblyExports, getConfig } = await dotnet
        .withModuleConfig({ onDownloadResourceProgress: onProgress })
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery()
        .create();

    const config = getConfig();

    // Input, frame and resize callbacks live in the DrawnUi.Wasm assembly (DrawnUi.Web is only the asset path).
    const lib = await getAssemblyExports('DrawnUi.Wasm');
    const Input = lib.DrawnUi.Draw.WebInput;
    const Super = lib.DrawnUi.Draw.Super;
    const Host = lib.DrawnUi.Draw.BrowserHost;

    setModuleExports({
        onBrowserFrame: Super.OnBrowserFrame,
        onCanvasResize: Host.OnCanvasResize,
        onPointerDown: Input.OnPointerDown,
        onPointerMove: Input.OnPointerMove,
        onPointerUp: Input.OnPointerUp,
        onPointerCancel: Input.OnPointerCancel,
        onWheel: Input.OnWheel,
        onContextMenu: Input.OnContextMenu,
        onKeyDown: Input.OnKeyDown,
        onKeyUp: Input.OnKeyUp,
    });

    const app = await getAssemblyExports(config.mainAssemblyName);
    const main = findExport(app, 'Main');
    if (!main) throw new Error(`No [JSExport] Main found in ${config.mainAssemblyName}`);
    main();

    hidePageLoader();
} catch (error) {
    console.error('Dungeon Run: failed to start:', error);
    loader.remove();
    showError('Failed to load: ' + error.message);
}
