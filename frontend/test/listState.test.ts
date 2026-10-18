import { describe, expect, it } from 'vitest'
import { resolveListState } from '../app/utils/listState'

describe('resolveListState', () => {
  it.each(['idle', 'pending'])('shows the skeleton before the first response (%s)', status => {
    expect(resolveListState({ status, hasError: false, hasData: false, itemCount: 0 })).toBe('loading')
  })

  it('shows the empty state when the response has no items', () => {
    expect(resolveListState({ status: 'success', hasError: false, hasData: true, itemCount: 0 })).toBe('empty')
  })

  it('shows the list when there are items', () => {
    expect(resolveListState({ status: 'success', hasError: false, hasData: true, itemCount: 3 })).toBe('ready')
  })

  it('keeps the previous list visible while filters reload it', () => {
    expect(resolveListState({ status: 'pending', hasError: false, hasData: true, itemCount: 3 })).toBe('ready')
  })

  it('shows the error even if old data is still there', () => {
    expect(resolveListState({ status: 'error', hasError: true, hasData: true, itemCount: 3 })).toBe('error')
    expect(resolveListState({ status: 'success', hasError: true, hasData: false, itemCount: 0 })).toBe('error')
  })
})
