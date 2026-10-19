import { describe, expect, it, vi } from 'vitest'
import { runOptimistic } from '../app/utils/optimistic'

describe('runOptimistic', () => {
  it('applies at once and keeps the change when the request succeeds', async () => {
    const state = { interest: 'None' }
    const onError = vi.fn()

    const ok = await runOptimistic({
      apply: () => {
        state.interest = 'Starred'
      },
      rollback: () => {
        state.interest = 'None'
      },
      request: async () => {
        expect(state.interest).toBe('Starred') // visible before the API answered
      },
      onError,
    })

    expect(ok).toBe(true)
    expect(state.interest).toBe('Starred')
    expect(onError).not.toHaveBeenCalled()
  })

  it('rolls back and reports the error when the API fails', async () => {
    const state = { isSeen: false }
    const failure = Object.assign(new Error('boom'), { statusCode: 500 })
    const onError = vi.fn()

    const ok = await runOptimistic({
      apply: () => {
        state.isSeen = true
      },
      rollback: () => {
        state.isSeen = false
      },
      request: () => Promise.reject(failure),
      onError,
    })

    expect(ok).toBe(false)
    expect(state.isSeen).toBe(false)
    expect(onError).toHaveBeenCalledWith(failure)
  })
})
