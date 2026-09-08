import workersUrl from "@opendaw/studio-core/workers-main.js?worker&url"
import workletsUrl from "@opendaw/studio-core/processors.js?url"
import wasmProcessorUrl from "@opendaw/studio-core-wasm/wasm-processor.js?url"
import wasmOfflineWorkerUrl from "@opendaw/studio-core-wasm/wasm-offline-worker.js?worker&url"
import {
    AudioWorklets, GlobalSampleLoaderManager, GlobalSoundfontLoaderManager, Workers
} from "@opendaw/studio-core"
import {WasmEngine} from "@opendaw/studio-core-wasm"
import {retryableOnce} from "../../src/retryable-once"

export type BootResult = {
    context: AudioContext
    env: unknown
    wasmReady: boolean
    sampleManager: GlobalSampleLoaderManager
    soundfontManager: GlobalSoundfontLoaderManager
}

// Провайдер, отдающий только то, что импортировали через import_asset; сеть не трогаем.
const makeProvider = (store: Map<string, {uuid: Uint8Array, data: unknown}>) => ({
    fetch: (uuid: Uint8Array) => {
        for (const entry of store.values()) {
            if (entry.uuid.every((byte, index) => byte === uuid[index])) {
                return Promise.resolve(entry.data as never)
            }
        }
        return Promise.reject(new Error("ассет не импортирован"))
    },
    invalidate: () => {}
})

export const assetStore = new Map<string, {uuid: Uint8Array, data: unknown, kind: "sample" | "soundfont"}>()

export const bootOpenDAW = retryableOnce(async (): Promise<BootResult> => {
    await Workers.install(workersUrl)
    AudioWorklets.install(workletsUrl)
    const context = new AudioContext({sampleRate: 48000, latencyHint: 0})
    const audioWorklets = await AudioWorklets.createFor(context)
    WasmEngine.install({
        processorUrl: wasmProcessorUrl,
        offlineWorkerUrl: wasmOfflineWorkerUrl,
        wasmUrl: "/wasm-engine"
    })
    const wasmReady = await WasmEngine.ensureReady(context)
    if (!wasmReady) {throw new Error("WASM-движок openDAW не поднялся")}
    const provider = makeProvider(assetStore as never)
    const sampleManager = new GlobalSampleLoaderManager(provider as never)
    const soundfontManager = new GlobalSoundfontLoaderManager(provider as never)
    const env = {
        audioContext: context, audioWorklets, sampleManager, soundfontManager,
        sampleService: provider, soundfontService: provider
    }
    return {context, env, wasmReady, sampleManager, soundfontManager}
})
