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
  page?: number
  pageSize?: number
}

export interface ImportResult {
  fetched: number
  added: number
  updated: number
  unchanged: number
  durationMs: number
}

/** RFC 9457 problem details as returned by the API on errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
