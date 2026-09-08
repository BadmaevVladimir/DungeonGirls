// Кэширует успех навсегда; отклонение сбрасывает кэш, чтобы следующий вызов повторил попытку.
export const retryableOnce = <T>(action: () => Promise<T>): (() => Promise<T>) => {
    let pending: Promise<T> | undefined
    return () => pending ??= action().catch(error => {
        pending = undefined
        throw error
    })
}
