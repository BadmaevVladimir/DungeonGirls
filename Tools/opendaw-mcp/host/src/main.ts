import {bootOpenDAW} from "./boot"

const api = {
    status: async () => {
        const {context, wasmReady} = await bootOpenDAW()
        return {crossOriginIsolated: self.crossOriginIsolated, wasmReady, sampleRate: context.sampleRate}
    }
}

declare global {
    interface Window {__odaw: typeof api}
}

window.__odaw = api
