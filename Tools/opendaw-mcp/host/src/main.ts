import {bootOpenDAW} from "./boot"
import {describeDevices} from "./describe"

const api = {
    status: async () => {
        const {context, wasmReady} = await bootOpenDAW()
        return {crossOriginIsolated: self.crossOriginIsolated, wasmReady, sampleRate: context.sampleRate}
    },
    describe: () => describeDevices()
}

declare global {
    interface Window {__odaw: typeof api}
}

window.__odaw = api
