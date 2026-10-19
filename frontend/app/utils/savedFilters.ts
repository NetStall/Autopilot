import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import { filtersFromQuery, filtersToQuery } from './vacancyFilters'

export const SAVED_FILTERS_KEY = 'job-autopilot:list-filters'

/** The part of Storage we use, so tests can pass a fake and broken storages are tolerated. */
export interface StorageLike {
  getItem: (key: string) => string | null
  setItem: (key: string, value: string) => void
}

/**
 * Last used list filters of this browser, normalised through the URL parser so a manipulated or outdated value
 * can only produce valid filters. Paging and the one-off „since last import" filter are not remembered.
 * Returns null when nothing (useful) is stored or storage is unavailable (private mode, blocked cookies).
 */
export function loadSavedFilters(storage: StorageLike | null | undefined): LocationQueryRaw | null {
  try {
    const raw = storage?.getItem(SAVED_FILTERS_KEY)
    if (!raw) {
      return null
    }
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return null
    }
    const query = filtersToQuery({ ...filtersFromQuery(parsed as LocationQuery), page: 1, since: '' })
    return Object.keys(query).length > 0 ? query : null
  }
  catch {
    return null
  }
}

export function saveFilters(storage: StorageLike | null | undefined, query: LocationQuery): void {
  try {
    const normalised = filtersToQuery({ ...filtersFromQuery(query), page: 1, since: '' })
    storage?.setItem(SAVED_FILTERS_KEY, JSON.stringify(normalised))
  }
  catch {
    // Storage full or disabled — remembering filters is a convenience only.
  }
}

/** window.localStorage, or null where it is not accessible. */
export function browserStorage(): StorageLike | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage
  }
  catch {
    return null
  }
}
