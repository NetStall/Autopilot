import type { RequiredGerman } from '~/types/vacancy'

/** What the list takes from the Suchprofil: default „Mindestbewertung", N of „Nur Top N", default German filter. */
export interface ListDefaultsState {
  minScore: number
  topN: number
  german: RequiredGerman[]
}

let pending: Promise<ListDefaultsState> | null = null

/**
 * Loads the defaults once per page load (and again after the Suchprofil was saved, see {@link invalidateListDefaults}).
 * Without a reachable API the neutral defaults are used.
 */
export function loadListDefaults(): Promise<ListDefaultsState> {
  pending ??= useVacanciesApi().searchProfile()
    .then(r => ({ minScore: r.profile.minScore, topN: r.profile.topN, german: r.profile.germanFilter ?? [] }))
    .catch(() => ({ minScore: 0, topN: 20, german: [] }))
  return pending
}

export function invalidateListDefaults() {
  pending = null
}
