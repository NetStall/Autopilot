// Mirrors the backend DTOs in JobAutopilot.Api/Features.

export type EmployerType = 'Unknown' | 'Direct' | 'Agency' | 'Temporary'

export type VacancySource = 'Arbeitsagentur' | 'Manual'

export type VacancySort = 'relevance' | 'published' | 'fetched'

export type RemoteLevel = 'FullRemote' | 'Hybrid' | 'OnSite' | 'Unknown'

export type RelevanceLevel = 'Relevant' | 'Maybe' | 'Irrelevant'

/** „Merken" / „Ausblenden" */
export type VacancyInterest = 'None' | 'Starred' | 'Dismissed'

export interface Vacancy {
  id: string
  source: VacancySource
  externalId: string
  title: string
  company: string
  location: string | null
  url: string | null
  /** Plain text; only in the detail response (the list leaves it out). Never render as HTML. */
  description: string | null
  employerType: EmployerType
  publishedAt: string | null
  fetchedAt: string
  isSeen: boolean
  remoteLevel: RemoteLevel
  /** Names of the searches that found this vacancy. */
  matchedSearches: string[]
  interest: VacancyInterest
  /** 0–100; null until scored. */
  relevanceScore: number | null
  relevanceLevel: RelevanceLevel | null
  /** Why: „C# im Titel ✓ (+50)", „Java als Hauptsprache ✗ (-60)", remote and employer notes. */
  relevanceReasons: string[]
  detailsFetchedAt: string | null
}

export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

/** Query parameters of GET /api/vacancies. */
export interface VacancyListParams {
  q?: string
  location?: string
  employerType?: EmployerType[]
  seen?: boolean
  source?: VacancySource
  sort?: VacancySort
  remote?: RemoteLevel[]
  relevance?: RelevanceLevel[]
  search?: string[]
  interest?: VacancyInterest[]
  includeDismissed?: boolean
  /** ISO timestamp: only vacancies that entered the system at or after it. */
  fetchedSince?: string
  page?: number
  pageSize?: number
}

export interface SearchImportResult {
  name: string
  keyword: string
  location: string | null
  remoteOnly: boolean
  fetched: number
  added: number
  updated: number
  unchanged: number
  excluded: number
}

export type ImportRunStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled'

export interface ImportCounts {
  fetched: number
  added: number
  updated: number
  unchanged: number
  excluded: number
}

/** GET /api/imports/{id} — one background import run. */
export interface ImportRun extends ImportCounts {
  id: string
  trigger: 'Manual' | 'Scheduled'
  status: ImportRunStatus
  requestedAt: string
  startedAt: string | null
  finishedAt: string | null
  durationMs: number | null
  searchesTotal: number
  searchesDone: number
  currentSearch: string | null
  error: string | null
  searches: SearchImportResult[]
}

/** POST /api/imports → 202 */
export interface ImportStarted {
  importId: string
}

/** GET/POST /api/vacancies/rescore */
export interface RescoreStatus {
  state: 'Idle' | 'Queued' | 'Running'
  lastStartedAt: string | null
  lastFinishedAt: string | null
  lastScored: number | null
  lastError: string | null
}

/** GET /api/settings/ui */
export interface UiSettings {
  highlightKeywords: string[]
}

/** A search from Import:Searches (GET /api/imports/searches). */
export interface ConfiguredSearch {
  name: string
  keyword: string
  location: string | null
  radiusKm: number | null
  publishedWithinDays: number
  remoteOnly: boolean
}

/** RFC 9457 problem details as returned by the API on errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
