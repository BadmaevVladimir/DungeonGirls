// Рендер диапазона отдаёт L + T сэмплов: хвост за границей лупа — это то, что в
// зацикленном воспроизведении звучит поверх начала. Складываем его туда, откуда он слышен.
export const foldTail = (channels: ReadonlyArray<Float32Array>, loopLength: number): Float32Array[] => {
    if (!Number.isInteger(loopLength) || loopLength <= 0) {
        throw new Error(`длина лупа должна быть положительным целым, получено ${loopLength}`)
    }
    return channels.map(input => {
        const out = new Float32Array(loopLength)
        out.set(input.subarray(0, Math.min(loopLength, input.length)))
        for (let offset = loopLength; offset < input.length; offset += loopLength) {
            const count = Math.min(loopLength, input.length - offset)
            for (let index = 0; index < count; index++) {
                const val = out[index]!
                out[index] = val + input[offset + index]!
            }
        }
        return out
    })
}
