import {afterAll, beforeAll, describe, expect, it} from "vitest"
import {HostBridge, HostLostError} from "../../src/bridge"

let bridge: HostBridge

beforeAll(async () => {bridge = await HostBridge.create("host/dist")}, 120_000)
afterAll(async () => {await bridge?.close()})

describe("HostBridge", () => {
    it("поднимает страницу и вызывает примитивы", async () => {
        const status = await bridge.call<{wasmReady: boolean}>("status")
        expect(status.wasmReady).toBe(true)
    })

    it("пробрасывает ошибку страницы с сообщением", async () => {
        await expect(bridge.call("render", {target: "mix"})).rejects.toThrow(/не собран/)
    })

    it("переживает смерть страницы и честно сообщает о потере состояния", async () => {
        await bridge.call("status")
        await bridge.killPageForTest()
        await expect(bridge.call("inspect")).rejects.toThrow(HostLostError)
        // после подъёма мост снова работает, но проект пуст
        expect(await bridge.call("inspect")).toBeNull()
        expect((await bridge.call<{wasmReady: boolean}>("status")).wasmReady).toBe(true)
    })
})
