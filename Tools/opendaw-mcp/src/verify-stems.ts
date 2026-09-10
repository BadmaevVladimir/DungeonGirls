// Приёмка музыкального набора: читает готовые wav с диска и проверяет инвариант слоёв.
// Отличается от test/integration/stems-sum.test.ts тем, что смотрит на реальные файлы
// трека, а не на синтетический проект внутри движка.

export type Decoded = {
    sampleRate: number
    channels: Float32Array[]
    // Разрядность исходного файла. Нужна вызывающему, чтобы выбрать допуск сравнения:
    // у int16 квантование даёт отклонение на три порядка выше, чем у float32.
    bitsPerSample?: number
}

export type StemSet = {
    mix: Decoded
    stems: ReadonlyArray<{name: string, wav: Decoded}>
    tolerance: number
}

export class WavError extends Error {}

const ascii = (bytes: Uint8Array, offset: number, length: number): string => {
    let text = ""
    for (let index = 0; index < length; index++) {text += String.fromCharCode(bytes[offset + index]!)}
    return text
}

// Чанки идут в произвольном порядке и между fmt и data может стоять что угодно
// (LIST, cue, fact), поэтому заголовок разбирается обходом, а не по фиксированным смещениям.
export const decodeWav = (bytes: Uint8Array): Decoded => {
    if (bytes.length < 12 || ascii(bytes, 0, 4) !== "RIFF" || ascii(bytes, 8, 4) !== "WAVE") {
        throw new WavError("не WAV: отсутствует заголовок RIFF/WAVE")
    }
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
    let format = 0
    let channelCount = 0
    let sampleRate = 0
    let bitsPerSample = 0
    let dataOffset = -1
    let dataSize = 0
    let offset = 12
    while (offset + 8 <= bytes.length) {
        const id = ascii(bytes, offset, 4)
        const size = view.getUint32(offset + 4, true)
        const body = offset + 8
        if (id === "fmt ") {
            format = view.getUint16(body, true)
            channelCount = view.getUint16(body + 2, true)
            sampleRate = view.getUint32(body + 4, true)
            bitsPerSample = view.getUint16(body + 14, true)
        } else if (id === "data") {
            dataOffset = body
            dataSize = Math.min(size, bytes.length - body)
        }
        offset = body + size + (size % 2)
    }
    if (channelCount === 0 || dataOffset < 0) {throw new WavError("в WAV нет чанка fmt или data")}
    const bytesPerSample = bitsPerSample / 8
    const frames = Math.floor(dataSize / (bytesPerSample * channelCount))
    const channels = Array.from({length: channelCount}, () => new Float32Array(frames))
    for (let frame = 0; frame < frames; frame++) {
        for (let channel = 0; channel < channelCount; channel++) {
            const at = dataOffset + (frame * channelCount + channel) * bytesPerSample
            let value: number
            if (format === 3 && bitsPerSample === 32) {
                value = view.getFloat32(at, true)
            } else if (format === 1 && bitsPerSample === 16) {
                value = view.getInt16(at, true) / 32768
            } else if (format === 1 && bitsPerSample === 24) {
                const raw = bytes[at]! | (bytes[at + 1]! << 8) | (bytes[at + 2]! << 16)
                value = ((raw & 0x800000) === 0 ? raw : raw - 0x1000000) / 8388608
            } else {
                throw new WavError(`формат ${format} c ${bitsPerSample} бит не поддержан`)
            }
            channels[channel]![frame] = value
        }
    }
    return {sampleRate, channels, bitsPerSample}
}

// Допуск сравнения суммы слоёв с миксом по разрядности файлов.
// Для int16 складываются два источника: округление каждого из N+1 файлов (полшага
// квантования) и асимметрия encodeWav, который масштабирует положительные отсчёты
// на 32767, а отрицательные на 32768 — это даёт относительную ошибку ещё в шаг.
// Итог для трёх слоёв и микса — около четырёх шагов; берём пять с запасом.
// Даже так порог остаётся около −72 dBFS, то есть далеко ниже слышимого.
export const toleranceFor = (bitsPerSample: number | undefined, stemCount: number): number => {
    if (bitsPerSample === 32) {return 1e-6}
    const step = bitsPerSample === 24 ? 1 / 8388608 : 1 / 32768
    return step * (stemCount + 2)
}

const peakOf = (wav: Decoded): number => {
    let peak = 0
    for (const channel of wav.channels) {
        for (const sample of channel) {
            const magnitude = Math.abs(sample)
            if (magnitude > peak) {peak = magnitude}
        }
    }
    return peak
}

const rmsOf = (wav: Decoded): number => {
    let squares = 0
    let count = 0
    for (const channel of wav.channels) {
        for (const sample of channel) {squares += sample * sample; count++}
    }
    return count === 0 ? 0 : Math.sqrt(squares / count)
}

const identical = (left: Decoded, right: Decoded): boolean => {
    if (left.channels.length !== right.channels.length) {return false}
    for (let channel = 0; channel < left.channels.length; channel++) {
        const a = left.channels[channel]!
        const b = right.channels[channel]!
        if (a.length !== b.length) {return false}
        for (let frame = 0; frame < a.length; frame++) {
            if (Math.abs(a[frame]! - b[frame]!) > 1e-9) {return false}
        }
    }
    return true
}

// Порог «слой не пустой». −80 dBFS: ниже этого слой неотличим от тишины даже
// на полной громкости, и его вклад в микс не услышать.
const SILENCE_RMS = 1e-4

export const checkStemSet = ({mix, stems, tolerance}: StemSet): string[] => {
    const problems: string[] = []
    if (stems.length === 0) {
        problems.push("нет стемов: проверять нечего")
        return problems
    }
    const frames = mix.channels[0]?.length ?? 0
    for (const {name, wav} of stems) {
        if (wav.sampleRate !== mix.sampleRate) {
            problems.push(`частота стема "${name}" (${wav.sampleRate}) не равна частоте микса (${mix.sampleRate})`)
        }
        if (wav.channels.length !== mix.channels.length) {
            problems.push(`число каналов стема "${name}" (${wav.channels.length}) не равно миксу (${mix.channels.length})`)
        }
        if ((wav.channels[0]?.length ?? 0) !== frames) {
            problems.push(`длина стема "${name}" (${wav.channels[0]?.length ?? 0}) не равна длине микса (${frames})`)
        }
        if (peakOf(wav) > 1) {problems.push(`перегруз в стеме "${name}": пик выше 1.0`)}
        if (rmsOf(wav) < SILENCE_RMS) {problems.push(`тишина в стеме "${name}": он ничего не вносит в микс`)}
    }
    if (peakOf(mix) > 1) {problems.push("перегруз в контрольном миксе: пик выше 1.0")}
    for (let left = 0; left < stems.length; left++) {
        for (let right = left + 1; right < stems.length; right++) {
            if (identical(stems[left]!.wav, stems[right]!.wav)) {
                problems.push(`стемы "${stems[left]!.name}" и "${stems[right]!.name}" совпадают отсчёт в отсчёт`)
            }
        }
    }
    // Сумму считаем только когда формы сошлись: иначе получим лавину ложных расхождений.
    if (problems.length === 0) {
        let worst = 0
        for (let channel = 0; channel < mix.channels.length; channel++) {
            for (let frame = 0; frame < frames; frame++) {
                let total = 0
                for (const {wav} of stems) {total += wav.channels[channel]![frame]!}
                worst = Math.max(worst, Math.abs(total - mix.channels[channel]![frame]!))
            }
        }
        if (worst > tolerance) {
            problems.push(`сумма стемов расходится с миксом: худшее отклонение ${worst.toExponential(2)} при допуске ${tolerance.toExponential(2)}`)
        }
    }
    return problems
}
