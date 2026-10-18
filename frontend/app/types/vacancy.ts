// Mirrors the backend DTOs in JobAutopilot.Api/Features.

export type EmployerType = 'Unknown' | 'Direct' | 'Agency' | 'Temporary'

export type VacancySource = 'Arbeitsagentur' | 'Manual'

export type VacancySort = 'published' | 'fetched'

export interface Vacancy {
  id: string
  source: VacancySource
  externalId: string
  title: string
  company: string
  location: string | null
  url: string | null
  description: string | null
  employerType: EmployerType
  publishedAt: string | null
  fetchedAt: string
  isSeen: boolean
  /** Found by at least one remote-only search. */
  isRemote: boolean
  /** Names of the searches that found this vacancy. */
  matchedSearches: string[]
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
  remote?: boolean
  search?: string[]
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

export interface ImportResult {
  fetched: number
  added: number
  updated: number
  unchanged: number
  excluded: number
  durationMs: number
  searches: SearchImportResult[]
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
