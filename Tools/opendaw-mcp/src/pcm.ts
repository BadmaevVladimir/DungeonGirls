// Node pools Buffers, so a view straight over buffer.buffer would read neighbouring memory; copy instead.
const decodeBytes = (base64: string): Uint8Array => {
    const buffer = Buffer.from(base64, "base64")
    const copy = new Uint8Array(buffer.length)
    copy.set(buffer)
    return copy
}

// Both sides assume little-endian, which holds for every platform this runs on.
export const decodeChannel = (base64: string): Float32Array => new Float32Array(decodeBytes(base64).buffer)

// Same base64 payload shape as PCM, but for opaque bytes (e.g. an .odb bundle) rather than samples.
export const decodeBundle = (base64: string): Uint8Array => decodeBytes(base64)
