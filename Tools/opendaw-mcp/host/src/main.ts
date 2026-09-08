import {bootOpenDAW} from "./boot"
import {describeDevices} from "./describe"
import {buildProject, currentSummary, resetProject} from "./build"

const api = {
    status: async () => {
        const {context, wasmReady} = await bootOpenDAW()
        return {crossOriginIsolated: self.crossOriginIsolated, wasmReady, sampleRate: context.sampleRate}
    },
    describe: () => describeDevices(),
    build: (flat: Parameters<typeof buildProject>[0]) => buildProject(flat),
    inspect: () => currentSummary(),
    reset: () => {resetProject()}
}

declare global {
    interface Window {__odaw: typeof api}
}

window.__odaw = api
