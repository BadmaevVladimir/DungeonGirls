// Shared by render.ts and assets.ts: page.evaluate serializes return values as JSON, so binary
// payloads must leave as base64 rather than number[] — a large payload would blow up into gigabytes of JSON text.
const CHUNK = 0x8000
export const bytesToBase64 = (bytes: Uint8Array): string => {
    let binary = ""
    for (let offset = 0; offset < bytes.length; offset += CHUNK) {
        binary += String.fromCharCode(...bytes.subarray(offset, offset + CHUNK))
    }
    return btoa(binary)
}
