import {bootOpenDAW} from "./boot"
import {describeDevices} from "./describe"
import {buildProject, currentDocumentName, currentSummary, resetProject} from "./build"
import {renderProject, type RenderRequest} from "./render"
import {exportBundle, importAsset, listAssets} from "./assets"

const api = {
    status: async () => {
        const {context, wasmReady} = await bootOpenDAW()
        return {crossOriginIsolated: self.crossOriginIsolated, wasmReady, sampleRate: context.sampleRate}
    },
    describe: () => describeDevices(),
    build: (flat: Parameters<typeof buildProject>[0]) => buildProject(flat),
    inspect: () => currentSummary(),
    reset: () => {resetProject()},
    render: (request: RenderRequest) => renderProject(request),
    importAsset: (name: string, kind: "sample" | "soundfont", url: string) => importAsset(name, kind, url),
    listAssets: () => listAssets(),
    bundle: (name?: string) => exportBundle(name ?? currentDocumentName() ?? "openDAW MCP")
}

declare global {
    interface Window {__odaw: typeof api}
}

window.__odaw = api
