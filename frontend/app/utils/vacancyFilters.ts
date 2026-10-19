import type { LocationQuery, LocationQueryRaw, LocationQueryValue } from 'vue-router'
import type {
  EmployerType,
  RelevanceLevel,
  RemoteLevel,
  VacancyListParams,
  VacancySort,
  VacancySource,
} from '../types/vacancy'

export const EMPLOYER_TYPES: readonly EmployerType[] = ['Direct', 'Agency', 'Temporary', 'Unknown']

/** Display and URL order. */
export const RELEVANCE_LEVELS: readonly RelevanceLevel[] = ['Relevant', 'Maybe', 'Irrelevant']

/** The default list: everything except „Irrelevant". */
export const DEFAULT_RELEVANCE: readonly RelevanceLevel[] = ['Relevant', 'Maybe']

export const VACANCY_SOURCES: readonly VacancySource[] = ['Arbeitsagentur', 'Manual']

/** Alle / Neu / Gesehen */
export type StatusFilter = 'all' | 'new' | 'seen'

/** Arbeitsort: alle / remote oder hybrid / nur voll remote */
export type RemoteFilter = 'any' | 'remote' | 'full'

/** The list filters as they live in the URL query. */
export interface VacancyFilters {
  q: string
  location: string
  employerTypes: EmployerType[]
  status: StatusFilter
  remote: RemoteFilter
  /** Relevance levels shown; never empty (an empty selection means the default). */
  relevance: RelevanceLevel[]
  /** One source or '' for all. */
  source: VacancySource | ''
  /** Names of configured searches; vacancies found by any of them. */
  searches: string[]
  /** „Gemerkt" — only starred vacancies. */
  starredOnly: boolean
  /** „Ausgeblendet anzeigen" — include dismissed vacancies (hidden by default). */
  showDismissed: boolean
  /** ISO timestamp for „Neu seit letztem Import"; empty = off. */
  since: string
  sort: VacancySort
  page: number
}

export const DEFAULT_FILTERS: Readonly<VacancyFilters> = Object.freeze({
  q: '',
  location: '',
  employerTypes: [],
  status: 'all',
  remote: 'any',
  relevance: [...DEFAULT_RELEVANCE],
  source: '',
  searches: [],
  starredOnly: false,
  showDismissed: false,
  since: '',
  sort: 'relevance',
  page: 1,
})

/** Fresh defaults (the frozen object shares its arrays). */
export function defaultFilters(): VacancyFilters {
  return { ...DEFAULT_FILTERS, employerTypes: [], relevance: [...DEFAULT_RELEVANCE], searches: [] }
}

type QueryValue = LocationQueryValue | LocationQueryValue[] | undefined

function firstValue(value: QueryValue): string {
  const first = Array.isArray(value) ? value[0] : value
  return typeof first === 'string' ? first : ''
}

function allValues(value: QueryValue): string[] {
  const values = Array.isArray(value) ? value : [value]
  return values.filter((v): v is string => typeof v === 'string' && v.length > 0)
}

function isIsoTimestamp(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T[\d:.]+(Z|[+-]\d{2}:\d{2})$/.test(value) && !Number.isNaN(Date.parse(value))
}

function oneOf<T extends string>(allowed: readonly T[], value: string): value is T {
  return (allowed as readonly string[]).includes(value)
}

function parseRemote(value: string): RemoteFilter {
  if (value === 'full' || value === 'remote') {
    return value
  }
  // „Nur Remote" of step 03 (remote=true) in old links and remembered filters.
  return value === 'true' ? 'remote' : 'any'
}

function parseSort(value: string): VacancySort {
  return value === 'published' || value === 'fetched' ? value : 'relevance'
}

function sameLevels(a: readonly RelevanceLevel[], b: readonly RelevanceLevel[]): boolean {
  return a.length === b.length && a.every(level => b.includes(level))
}

/** Reads filters from the URL; unknown or malformed values fall back to the defaults. */
export function filtersFromQuery(query: LocationQuery): VacancyFilters {
  const status = firstValue(query.status)
  const source = firstValue(query.source)
  const page = Number.parseInt(firstValue(query.page), 10)
  const relevance = RELEVANCE_LEVELS.filter(level => allValues(query.relevance).includes(level))

  return {
    q: firstValue(query.q).trim(),
    location: firstValue(query.location).trim(),
    employerTypes: [...new Set(allValues(query.employerType).filter(t => oneOf(EMPLOYER_TYPES, t)))],
    status: status === 'new' || status === 'seen' ? status : 'all',
    remote: parseRemote(firstValue(query.remote)),
    relevance: relevance.length > 0 ? relevance : [...DEFAULT_RELEVANCE],
    source: oneOf(VACANCY_SOURCES, source) ? source : '',
    starredOnly: firstValue(query.starred) === 'true',
    showDismissed: firstValue(query.dismissed) === 'show',
    since: isIsoTimestamp(firstValue(query.since)) ? firstValue(query.since) : '',
    searches: [...new Set(allValues(query.search).map(s => s.trim()).filter(s => s.length > 0))],
    sort: parseSort(firstValue(query.sort)),
    page: Number.isInteger(page) && page > 0 ? page : 1,
  }
}

/** Writes filters to the URL; defaults are left out so a clean list has a clean URL. */
export function filtersToQuery(filters: VacancyFilters): LocationQueryRaw {
  const query: LocationQueryRaw = {}
  const q = filters.q.trim()
  const location = filters.location.trim()

  if (q) {
    query.q = q
  }
  if (location) {
    query.location = location
  }
  if (filters.employerTypes.length > 0) {
    query.employerType = EMPLOYER_TYPES.filter(t => filters.employerTypes.includes(t))
  }
  if (filters.status !== 'all') {
    query.status = filters.status
  }
  if (filters.remote !== 'any') {
    query.remote = filters.remote
  }
  if (filters.relevance.length > 0 && !sameLevels(filters.relevance, DEFAULT_RELEVANCE)) {
    query.relevance = RELEVANCE_LEVELS.filter(level => filters.relevance.includes(level))
  }
  if (filters.source) {
    query.source = filters.source
  }
  if (filters.searches.length > 0) {
    query.search = [...filters.searches]
  }
  if (filters.starredOnly) {
    query.starred = 'true'
  }
  if (filters.showDismissed) {
    query.dismissed = 'show'
  }
  if (filters.since) {
    query.since = filters.since
  }
  if (filters.sort !== 'relevance') {
    query.sort = filters.sort
  }
  if (filters.page > 1) {
    query.page = String(filters.page)
  }
  return query
}

const remoteLevels: Record<Exclude<RemoteFilter, 'any'>, RemoteLevel[]> = {
  remote: ['FullRemote', 'Hybrid'],
  full: ['FullRemote'],
}

/** Maps the UI filters to the API's query parameters. */
export function filtersToApiParams(filters: VacancyFilters, pageSize: number): VacancyListParams {
  const params: VacancyListParams = { sort: filters.sort, page: filters.page, pageSize }
  if (filters.q.trim()) {
    params.q = filters.q.trim()
  }
  if (filters.location.trim()) {
    params.location = filters.location.trim()
  }
  if (filters.employerTypes.length > 0) {
    params.employerType = [...filters.employerTypes]
  }
  if (filters.status !== 'all') {
    params.seen = filters.status === 'seen'
  }
  if (filters.remote !== 'any') {
    params.remote = [...remoteLevels[filters.remote]]
  }
  // All three levels = no restriction.
  if (filters.relevance.length > 0 && filters.relevance.length < RELEVANCE_LEVELS.length) {
    params.relevance = RELEVANCE_LEVELS.filter(level => filters.relevance.includes(level))
  }
  if (filters.source) {
    params.source = filters.source
  }
  if (filters.searches.length > 0) {
    params.search = [...filters.searches]
  }
  if (filters.starredOnly) {
    params.interest = ['Starred']
  }
  else if (filters.showDismissed) {
    params.includeDismissed = true
  }
  if (filters.since) {
    params.fetchedSince = filters.since
  }
  return params
}

/** True if anything other than sorting, paging and the default relevance narrows the list. */
export function hasActiveFilters(filters: VacancyFilters): boolean {
  return filters.q.trim() !== ''
    || filters.location.trim() !== ''
    || filters.employerTypes.length > 0
    || filters.status !== 'all'
    || filters.remote !== 'any'
    || !sameLevels(filters.relevance, DEFAULT_RELEVANCE)
    || filters.source !== ''
    || filters.searches.length > 0
    || filters.starredOnly
    || filters.showDismissed
    || filters.since !== ''
}
