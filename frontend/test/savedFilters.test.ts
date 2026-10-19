import { describe, expect, it } from 'vitest'
import { loadSavedFilters, SAVED_FILTERS_KEY, saveFilters, type StorageLike } from '../app/utils/savedFilters'

function memoryStorage(initial: Record<string, string> = {}): StorageLike & { data: Record<string, string> } {
  const data: Record<string, string> = { ...initial }
  return {
    data,
    getItem: key => data[key] ?? null,
    setItem: (key, value) => {
      data[key] = value
    },
  }
}

describe('saved list filters', () => {
  it('remembers the filters but not the page or the one-off since filter', () => {
    const storage = memoryStorage()

    saveFilters(storage, { q: 'vue', remote: 'full', page: '3', since: '2026-10-19T05:00:00Z' })

    expect(JSON.parse(storage.data[SAVED_FILTERS_KEY]!)).toEqual({ q: 'vue', remote: 'full' })
    expect(loadSavedFilters(storage)).toEqual({ q: 'vue', remote: 'full' })
  })

  it('upgrades remembered filters of step 03 (remote=true)', () => {
    const storage = memoryStorage({ [SAVED_FILTERS_KEY]: JSON.stringify({ remote: 'true', sort: 'published' }) })

    expect(loadSavedFilters(storage)).toEqual({ remote: 'remote', sort: 'published' })
  })

  it('returns null when nothing useful is stored', () => {
    expect(loadSavedFilters(memoryStorage())).toBeNull()
    expect(loadSavedFilters(memoryStorage({ [SAVED_FILTERS_KEY]: '{}' }))).toBeNull()
    expect(loadSavedFilters(null)).toBeNull()
  })

  it('normalises manipulated or broken values', () => {
    expect(loadSavedFilters(memoryStorage({ [SAVED_FILTERS_KEY]: 'not json' }))).toBeNull()
    expect(loadSavedFilters(memoryStorage({ [SAVED_FILTERS_KEY]: '[1,2]' }))).toBeNull()
    expect(loadSavedFilters(memoryStorage({
      [SAVED_FILTERS_KEY]: JSON.stringify({ employerType: ['Agency', 'Hacker'], status: 'everything', starred: 'true' }),
    }))).toEqual({ employerType: ['Agency'], starred: 'true' })
  })

  it('survives a storage that throws (private mode, blocked cookies)', () => {
    const throwing: StorageLike = {
      getItem: () => {
        throw new Error('SecurityError')
      },
      setItem: () => {
        throw new Error('QuotaExceededError')
      },
    }

    expect(loadSavedFilters(throwing)).toBeNull()
    expect(() => saveFilters(throwing, { q: 'x' })).not.toThrow()
  })
})
