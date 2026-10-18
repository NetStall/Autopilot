export type ListState = 'loading' | 'error' | 'empty' | 'ready'

export interface ListStateInput {
  /** useAsyncData status: 'idle' | 'pending' | 'success' | 'error' */
  status: string
  hasError: boolean
  /** Whether a response has arrived at least once (kept while refetching). */
  hasData: boolean
  itemCount: number
}

/**
 * Which of the four list views to show. An error wins over stale data. The skeleton is only shown before the
 * first response; while filters change, the previous result stays visible instead of flashing a skeleton.
 */
export function resolveListState({ status, hasError, hasData, itemCount }: ListStateInput): ListState {
  if (hasError || status === 'error') {
    return 'error'
  }
  if (!hasData) {
    return 'loading'
  }
  return itemCount === 0 ? 'empty' : 'ready'
}
