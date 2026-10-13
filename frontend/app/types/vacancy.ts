// Mirrors the backend DTOs in JobAutopilot.Api/Features/Vacancies.

export type EmployerType = 'Unknown' | 'Direct' | 'Agency' | 'Temporary'

export type VacancySource = 'Arbeitsagentur' | 'Manual'

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
