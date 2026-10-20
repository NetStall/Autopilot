import type { LocationQuery, LocationQueryRaw, LocationQueryValue } from 'vue-router'
import type {
  EmployerType,
  RelevanceLevel,
  RemoteLevel,
  RequiredGerman,
  VacancyFacets,
  VacancyListParams,
  VacancySort,
  VacancySource,
} from '../types/vacancy'

export const EMPLOYER_TYPES: readonly EmployerType[] = ['Direct', 'Agency', 'Temporary', 'Unknown']

/** Display and URL order. */
export const RELEVANCE_LEVELS: readonly RelevanceLevel[] = ['Relevant', 'Maybe', 'Irrelevant']

/** The default list: everything except „Irrelevant". */
export const DEFAULT_RELEVANCE: readonly RelevanceLevel[] = ['Relevant', 'Maybe']

export const VACANCY_SOURCES: readonly VacancySource[] = ['Arbeitsagentur', 'Himalayas', 'Manual']

/** „Deutsch erforderlich", strongest requirement first. */
export const REQUIRED_GERMAN: readonly RequiredGerman[] = ['C1Plus', 'B2', 'B1OrLower', 'None', 'Unknown']

const GERMAN_LABELS: Record<RequiredGerman, string> = {
  C1Plus: 'C1 oder höher',
  B2: 'B2',
  B1OrLower: 'B1 oder niedriger',
  None: 'Kein Deutsch nötig',
  Unknown: 'Unbekannt',
}

export function requiredGermanLabel(value: RequiredGerman): string {
  return GERMAN_LABELS[value]
}

/** Steps of the „Mindestbewertung" slider. */
export const MIN_SCORE_STEP = 5

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
  /** „Deutsch erforderlich"; null = not in the URL (the Suchprofil's default applies). */
  german: RequiredGerman[] | null
  /** Sources shown; empty = all. */
  sources: VacancySource[]
  /** „Mindestbewertung"; <c>null</c> = the default of the Suchprofil. */
  minScore: number | null
  /** „Nur Top N": N, or <c>null</c> = off. */
  top: number | null
  /** Names of configured searches; vacancies found by any of them. */
  searches: string[]
  /** „Gemerkt" — only starred vacancies. */
  starredOnly: boolean
  /** „Ausgeblendet anzeigen" — include dismissed vacancies (hidden by default). */
  showDismissed: boolean
  /** „Nicht erreichbare anzeigen" — include vacancies the Suchprofil hides as not reachable. */
  showUnreachable: boolean
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
  german: null,
  sources: [],
  minScore: null,
  top: null,
  searches: [],
  starredOnly: false,
  showDismissed: false,
  showUnreachable: false,
  since: '',
  sort: 'relevance',
  page: 1,
})

/** Fresh defaults (the frozen object shares its arrays). */
export function defaultFilters(): VacancyFilters {
  return { ...DEFAULT_FILTERS, employerTypes: [], relevance: [...DEFAULT_RELEVANCE], sources: [], searches: [] }
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

/** An integer within [min, max], otherwise <c>null</c>. */
function parseInt0(value: string, min: number, max: number): number | null {
  if (!/^\d{1,3}$/.test(value)) {
    return null
  }
  const n = Number.parseInt(value, 10)
  return n >= min && n <= max ? n : null
}

/** Rounds to the slider's steps and clamps to 0–100. */
export function normalizeMinScore(value: number): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return Math.min(100, Math.max(0, Math.round(value / MIN_SCORE_STEP) * MIN_SCORE_STEP))
}

function sameLevels(a: readonly RelevanceLevel[], b: readonly RelevanceLevel[]): boolean {
  return a.length === b.length && a.every(level => b.includes(level))
}

/** Reads filters from the URL; unknown or malformed values fall back to the defaults. */
export function filtersFromQuery(query: LocationQuery): VacancyFilters {
  const status = firstValue(query.status)
  const page = Number.parseInt(firstValue(query.page), 10)
  const relevance = RELEVANCE_LEVELS.filter(level => allValues(query.relevance).includes(level))
  const minScore = parseInt0(firstValue(query.minScore), 0, 100)

  return {
    q: firstValue(query.q).trim(),
    location: firstValue(query.location).trim(),
    employerTypes: [...new Set(allValues(query.employerType).filter(t => oneOf(EMPLOYER_TYPES, t)))],
    status: status === 'new' || status === 'seen' ? status : 'all',
    remote: parseRemote(firstValue(query.remote)),
    relevance: relevance.length > 0 ? relevance : [...DEFAULT_RELEVANCE],
    german: parseGerman(query.german),
    sources: VACANCY_SOURCES.filter(s => allValues(query.source).includes(s)),
    minScore: minScore == null ? null : normalizeMinScore(minScore),
    top: parseInt0(firstValue(query.top), 1, 200),
    starredOnly: firstValue(query.starred) === 'true',
    showDismissed: firstValue(query.dismissed) === 'show',
    showUnreachable: firstValue(query.unreachable) === 'show',
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
  if (filters.sources.length > 0) {
    query.source = VACANCY_SOURCES.filter(s => filters.sources.includes(s))
  }
  if (filters.german != null && filters.german.length > 0) {
    query.german = REQUIRED_GERMAN.filter(g => filters.german!.includes(g)).join(',')
  }
  if (filters.minScore != null) {
    query.minScore = String(filters.minScore)
  }
  if (filters.top != null) {
    query.top = String(filters.top)
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
  if (filters.showUnreachable) {
    query.unreachable = 'show'
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

/** What the Suchprofil contributes when the URL does not say otherwise. */
export interface ListDefaults {
  minScore: number
  /** Suchprofil default of „Deutsch erforderlich"; empty = all. */
  german?: readonly RequiredGerman[]
}

/** The German levels in effect: the URL's, else the Suchprofil's (empty = no restriction). */
export function effectiveGerman(filters: VacancyFilters, defaults?: ListDefaults): RequiredGerman[] {
  return [...(filters.german ?? defaults?.german ?? [])]
}

/** Toggles one level; an empty selection means „all", so unchecking starts from all five. */
export function toggleGerman(current: readonly RequiredGerman[], level: RequiredGerman, checked: boolean): RequiredGerman[] {
  const selected = new Set(current.length === 0 ? REQUIRED_GERMAN : current)
  if (checked) {
    selected.add(level)
  }
  else {
    selected.delete(level)
  }
  return REQUIRED_GERMAN.filter(g => selected.has(g))
}

/** Same restriction; all five levels = none. */
export function sameGerman(a: readonly RequiredGerman[], b: readonly RequiredGerman[]): boolean {
  const norm = (list: readonly RequiredGerman[]) => (list.length === REQUIRED_GERMAN.length ? [] : REQUIRED_GERMAN.filter(g => list.includes(g)))
  return JSON.stringify(norm(a)) === JSON.stringify(norm(b))
}

/** `german=B2,B1OrLower` or repeated keys; unknown values are dropped, null when nothing valid is left. */
function parseGerman(value: QueryValue): RequiredGerman[] | null {
  const values = allValues(value).flatMap(v => v.split(',')).map(v => v.trim())
  const valid = REQUIRED_GERMAN.filter(g => values.includes(g))
  return valid.length > 0 ? valid : null
}

/** The minimum score in effect: the URL's, else the Suchprofil's. */
export function effectiveMinScore(filters: VacancyFilters, defaults?: ListDefaults): number {
  return filters.minScore ?? defaults?.minScore ?? 0
}

/** Maps the UI filters to the API's query parameters. */
export function filtersToApiParams(filters: VacancyFilters, pageSize: number, defaults?: ListDefaults): VacancyListParams {
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
  if (filters.sources.length > 0 && filters.sources.length < VACANCY_SOURCES.length) {
    params.source = VACANCY_SOURCES.filter(s => filters.sources.includes(s))
  }
  const german = effectiveGerman(filters, defaults)
  if (german.length > 0 && german.length < REQUIRED_GERMAN.length) {
    params.german = REQUIRED_GERMAN.filter(g => german.includes(g))
  }
  const minScore = effectiveMinScore(filters, defaults)
  if (minScore > 0) {
    params.minScore = minScore
  }
  if (filters.top != null) {
    params.top = filters.top
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
  if (filters.showUnreachable) {
    params.includeUnreachable = true
  }
  if (filters.since) {
    params.fetchedSince = filters.since
  }
  return params
}

/** True if anything other than sorting, paging and the defaults narrows the list. */
export function hasActiveFilters(filters: VacancyFilters): boolean {
  return filters.q.trim() !== ''
    || filters.location.trim() !== ''
    || filters.employerTypes.length > 0
    || filters.status !== 'all'
    || filters.remote !== 'any'
    || !sameLevels(filters.relevance, DEFAULT_RELEVANCE)
    || filters.sources.length > 0
    || filters.german != null
    || filters.minScore != null
    || filters.top != null
    || filters.searches.length > 0
    || filters.starredOnly
    || filters.showDismissed
    || filters.showUnreachable
    || filters.since !== ''
}

/** „ (12)" behind a filter option, or nothing while the counts are not loaded. */
export function countSuffix(facets: VacancyFacets | null | undefined, value: number | undefined): string {
  return facets ? ` (${value ?? 0})` : ''
}

/** Count of a „Remote" option: voll remote, remote or hybrid, or all levels for „egal". */
export function remoteFacetCount(levels: VacancyFacets['remoteLevel'], option: RemoteFilter): number {
  const full = levels.FullRemote ?? 0
  if (option === 'full') {
    return full
  }
  if (option === 'remote') {
    return full + (levels.Hybrid ?? 0)
  }
  return Object.values(levels).reduce((a, b) => a + (b ?? 0), 0)
}

/** „258 Stellen" or „Top 20 von 258". */
export function totalLabel(totalCount: number, totalMatching: number, top: number | null): string {
  if (top != null) {
    return `Top ${Math.min(top, totalMatching)} von ${totalMatching}`
  }
  return `${totalCount} ${totalCount === 1 ? 'Stelle' : 'Stellen'}`
}
