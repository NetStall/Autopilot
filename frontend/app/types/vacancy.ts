import type { AiFitScore, VacancyExtraction } from './extraction'

// Mirrors the backend DTOs in JobAutopilot.Api/Features.

import type { AppliedInfo } from './application'

export type EmployerType = 'Unknown' | 'Direct' | 'Agency' | 'Temporary'

export type VacancySource = 'Arbeitsagentur' | 'Himalayas' | 'Manual'

/** The same job in another place (other source or repost). */
export interface VacancyLink {
  id: string
  source: VacancySource
  url: string | null
}

/** combined = rank (rules + extraction), rules = without any AI input, ai = „KI-Bewertung“ (07c). */
export type VacancySort = 'combined' | 'rules' | 'ai' | 'published' | 'fetched'

export type RemoteLevel = 'FullRemote' | 'Hybrid' | 'OnSite' | 'Unknown'

export type RelevanceLevel = 'Relevant' | 'Maybe' | 'Irrelevant'

/** German the posting asks for (filter „Deutsch erforderlich"). */
export type RequiredGerman = 'C1Plus' | 'B2' | 'B1OrLower' | 'None' | 'Unknown'

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
  /** Relevance minus the fit penalties — the order of the default list. */
  rankScore: number | null
  relevanceLevel: RelevanceLevel | null
  /** Why: „C# im Titel ✓ (+50)", „Java als Hauptsprache ✗ (-60)", remote and employer notes. */
  relevanceReasons: string[]
  detailsFetchedAt: string | null
  alsoFoundAt: VacancyLink[]
  /** „Beworben": an application exists for this job (or the same company with a similar title). */
  applied: AppliedInfo | null
  requiredGerman: RequiredGerman
  /** Practically not reachable (outside my places, not fully remote, at most 60 % home office). */
  unreachable: boolean
  /** The home-office share the source states as a field, e.g. 40 for „Homeoffice (bis zu 40 %)". */
  homeOfficePercent: number | null
  /** The description was pasted by hand („Beschreibung einfügen"). */
  descriptionIsManual: boolean
  /** The model's extraction for the current description (detail only, step 07a). */
  extraction?: VacancyExtraction | null
  /** My judgement „Passt" (true) / „Passt nicht" (false); null = not labelled (detail only). */
  goldLabel?: boolean | null
  /** What the remote badge is based on, e.g. „Homeoffice: bis 40 % (Feld)" or „Homeoffice: teilweise (laut Text)". */
  remoteNote?: string | null
  /** „KI-Bewertung" 0–100 of the local model (07b); shown next to the rank, not part of it. */
  aiScore?: number | null
  /** The rank without any AI input (sort „Nur Regeln“); equals rankScore without an extraction. */
  ruleRankScore?: number | null
  /** Verdict, reasons and blockers (detail only). */
  aiFitScore?: AiFitScore | null
}

export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

/** Counts per filter value under all other active filters. */
export interface VacancyFacets {
  source: Partial<Record<VacancySource, number>>
  remoteLevel: Partial<Record<RemoteLevel, number>>
  employerType: Partial<Record<EmployerType, number>>
  relevanceLevel: Partial<Record<RelevanceLevel, number>>
  requiredGerman: Partial<Record<RequiredGerman, number>>
}

/** GET /api/vacancies */
export interface VacancyPage extends PagedResponse<Vacancy> {
  /** Before the „Top N" cut — „Top 20 von 258". */
  totalMatching: number
  top: number | null
  facets: VacancyFacets
}

/** Query parameters of GET /api/vacancies. */
export interface VacancyListParams {
  q?: string
  location?: string
  employerType?: EmployerType[]
  seen?: boolean
  source?: VacancySource[]
  minScore?: number
  top?: number
  sort?: VacancySort
  remote?: RemoteLevel[]
  relevance?: RelevanceLevel[]
  german?: RequiredGerman[]
  search?: string[]
  interest?: VacancyInterest[]
  includeDismissed?: boolean
  includeUnreachable?: boolean
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
  source: VacancySource
  /** Set when the search was skipped because its source failed. */
  error: string | null
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

export type FitCoverage = 'Covered' | 'Partial' | 'Gap'

export interface FitRequirement {
  name: string
  coverage: FitCoverage
  optional: boolean
  /** Level, or the honest phrasing for a gap. */
  note: string
}

/** GET /api/vacancies/{id}/fit — „Passt zu mir?" */
export interface FitResponse {
  profileStatus: 'Loaded' | 'Missing' | 'Invalid'
  hasDescription: boolean
  requirements: FitRequirement[]
  warnings: { text: string, penalty: number }[]
  requiredGerman: string | null
  requiredYears: number | null
  penalty: number
  /** Where the requirements come from: the model's extraction or the rules. */
  source: 'Rules' | 'Ai'
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
  /** Score badge: from here on „hoch" … */
  scoreHigh: number
  /** … from here on „mittel". */
  scoreMedium: number
}

/** A search from Import:Searches (GET /api/imports/searches). */
export interface ConfiguredSearch {
  name: string
  keyword: string
  location: string | null
  radiusKm: number | null
  publishedWithinDays: number
  remoteOnly: boolean
  source: VacancySource
}

/** RFC 9457 problem details as returned by the API on errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
