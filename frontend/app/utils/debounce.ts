export interface Debounced<A extends unknown[]> {
  (...args: A): void
  cancel: () => void
}

/** Calls `fn` once `delayMs` after the last call. */
export function debounce<A extends unknown[]>(fn: (...args: A) => void, delayMs: number): Debounced<A> {
  let timer: ReturnType<typeof setTimeout> | undefined

  const debounced = (...args: A) => {
    clearTimeout(timer)
    timer = setTimeout(() => fn(...args), delayMs)
  }
  debounced.cancel = () => clearTimeout(timer)

  return debounced
}
