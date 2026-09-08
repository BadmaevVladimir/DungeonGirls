import {describe, it, expect} from "vitest"
import {retryableOnce} from "../src/retryable-once"

describe("retryableOnce", () => {
    it("two sequential successful calls invoke the underlying action exactly once and return the same value", async () => {
        let invocationCount = 0
        const action = async () => {
            invocationCount++
            return "success"
        }
        const wrapped = retryableOnce(action)
        const result1 = await wrapped()
        const result2 = await wrapped()

        expect(invocationCount).toBe(1)
        expect(result1).toBe("success")
        expect(result2).toBe("success")
    })

    it("concurrent callers arriving while a boot is in flight share one attempt", async () => {
        let invocationCount = 0
        const action = async () => {
            invocationCount++
            return "shared"
        }
        const wrapped = retryableOnce(action)
        const promise1 = wrapped()
        const promise2 = wrapped()
        const [result1, result2] = await Promise.all([promise1, promise2])

        expect(invocationCount).toBe(1)
        expect(result1).toBe("shared")
        expect(result2).toBe("shared")
    })

    it("after a rejection the next call retries", async () => {
        let invocationCount = 0
        const action = async () => {
            invocationCount++
            if (invocationCount === 1) {
                throw new Error("transient failure")
            }
            return "recovered"
        }
        const wrapped = retryableOnce(action)

        // First attempt rejects
        await expect(wrapped()).rejects.toThrow("transient failure")
        expect(invocationCount).toBe(1)

        // Second call runs the action again
        const result = await wrapped()
        expect(result).toBe("recovered")
        expect(invocationCount).toBe(2)
    })

    it("once it succeeds after an earlier failure, the success is cached", async () => {
        let invocationCount = 0
        const action = async () => {
            invocationCount++
            if (invocationCount === 1) {
                throw new Error("first attempt fails")
            }
            return "final result"
        }
        const wrapped = retryableOnce(action)

        // First attempt rejects
        await expect(wrapped()).rejects.toThrow("first attempt fails")
        // Second attempt succeeds
        const result2 = await wrapped()
        expect(result2).toBe("final result")
        // Third call does not re-run the action
        const result3 = await wrapped()
        expect(result3).toBe("final result")
        expect(invocationCount).toBe(2)
    })
})
