let _renderCounter = 0;
let _lastContainerId = null;
let _lastDefinition = null;
let _themeObserver = null;

function isDarkMode() {
    return document.documentElement.classList.contains('dark');
}

async function _doRender(containerId, definition) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const theme = isDarkMode() ? 'dark' : 'default';
    mermaid.initialize({ startOnLoad: false, theme });

    try {
        const graphId = `mermaid-graph-${_renderCounter++}`;
        const { svg } = await mermaid.render(graphId, definition);
        container.innerHTML = svg;
    } catch (err) {
        console.error('renderMermaid error:', err);
        container.innerHTML =
            '<p style="color:red;padding:1rem">Failed to render diagram. Check console for details.</p>';
    }
}

/**
 * Renders a Mermaid diagram and watches for dark/light mode changes to re-render automatically.
 */
async function renderMermaid(containerId, definition) {
    _lastContainerId = containerId;
    _lastDefinition = definition;

    await _doRender(containerId, definition);

    // Re-render whenever html.dark class is toggled
    if (_themeObserver) _themeObserver.disconnect();
    _themeObserver = new MutationObserver(async () => {
        if (_lastContainerId && _lastDefinition) {
            await _doRender(_lastContainerId, _lastDefinition);
        }
    });
    _themeObserver.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class']
    });
}
