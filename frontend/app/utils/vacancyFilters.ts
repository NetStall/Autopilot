import type { LocationQuery, LocationQueryRaw, LocationQueryValue } from 'vue-router'
import type { EmployerType, VacancyListParams, VacancySort } from '../types/vacancy'

export const EMPLOYER_TYPES: readonly EmployerType[] = ['Direct', 'Agency', 'Temporary', 'Unknown']

/** Alle / Neu / Gesehen */
export type StatusFilter = 'all' | 'new' | 'seen'

/** The list filters as they live in the URL query. */
export interface VacancyFilters {
  q: string
  location: string
  employerTypes: EmployerType[]
  status: StatusFilter
  /** „Nur Remote" */
  remoteOnly: boolean
  /** Names of configured searches; vacancies found by any of them. */
  searches: string[]
  sort: VacancySort
  page: number
}

export const DEFAULT_FILTERS: Readonly<VacancyFilters> = Object.freeze({
  q: '',
  location: '',
  employerTypes: [],
  status: 'all',
  remoteOnly: false,
  searches: [],
  sort: 'published',
  page: 1,
})

type QueryValue = LocationQueryValue | LocationQueryValue[] | undefined

function firstValue(value: QueryValue): string {
  const first = Array.isArray(value) ? value[0] : value
  return typeof first === 'string' ? first : ''
}

function allValues(value: QueryValue): string[] {
  const values = Array.isArray(value) ? value : [value]
  return values.filter((v): v is string => typeof v === 'string' && v.length > 0)
}

function isEmployerType(value: string): value is EmployerType {
  return (EMPLOYER_TYPES as readonly string[]).includes(value)
}

/** Reads filters from the URL; unknown or malformed values fall back to the defaults. */
export function filtersFromQuery(query: LocationQuery): VacancyFilters {
  const status = firstValue(query.status)
  const sort = firstValue(query.sort)
  const page = Number.parseInt(firstValue(query.page), 10)

  return {
    q: firstValue(query.q).trim(),
    location: firstValue(query.location).trim(),
    employerTypes: [...new Set(allValues(query.employerType).filter(isEmployerType))],
    status: status === 'new' || status === 'seen' ? status : 'all',
    remoteOnly: firstValue(query.remote) === 'true',
    searches: [...new Set(allValues(query.search).map(s => s.trim()).filter(s => s.length > 0))],
    sort: sort === 'fetched' ? 'fetched' : 'published',
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
  if (filters.remoteOnly) {
    query.remote = 'true'
  }
  if (filters.searches.length > 0) {
    query.search = [...filters.searches]
  }
  if (filters.sort !== 'published') {
    query.sort = filters.sort
  }
  if (filters.page > 1) {
    query.page = String(filters.page)
  }
  return query
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
  if (filters.remoteOnly) {
    params.remote = true
  }
  if (filters.searches.length > 0) {
    params.search = [...filters.searches]
  }
  return params
}

/** True if anything other than sorting and paging narrows the list. */
export function hasActiveFilters(filters: VacancyFilters): boolean {
  return filters.q.trim() !== ''
    || filters.location.trim() !== ''
    || filters.employerTypes.length > 0
    || filters.status !== 'all'
    || filters.remoteOnly
    || filters.searches.length > 0
}
