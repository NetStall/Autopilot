export interface OptimisticUpdate<T> {
  /** Show the new state at once. */
  apply: () => void
  /** Put the previous state back if the request fails. */
  rollback: () => void
  request: () => Promise<T>
  /** Called with the error after the rollback (e.g. to show a toast). */
  onError: (error: unknown) => void
}

/** Optimistic UI: apply, send, and roll back with an error callback if the API refuses. Returns whether it succeeded. */
export async function runOptimistic<T>({ apply, rollback, request, onError }: OptimisticUpdate<T>): Promise<boolean> {
  apply()
  try {
    await request()
    return true
  }
  catch (error: unknown) {
    rollback()
    onError(error)
    return false
  }
}
