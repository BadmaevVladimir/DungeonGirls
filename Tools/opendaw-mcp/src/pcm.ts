// Both sides assume little-endian, which holds for every platform this runs on.
export const decodeChannel = (base64: string): Float32Array => {
    const buffer = Buffer.from(base64, "base64")
    // Node pools Buffers, so a view straight over buffer.buffer would read neighbouring memory; copy instead.
    const copy = new Uint8Array(buffer.length)
    copy.set(buffer)
    return new Float32Array(copy.buffer)
}
