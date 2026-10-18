import { describe, expect, it } from 'vitest'
import {
  DEFAULT_FILTERS,
  filtersFromQuery,
  filtersToApiParams,
  filtersToQuery,
  hasActiveFilters,
  type VacancyFilters,
} from '../app/utils/vacancyFilters'

const full: VacancyFilters = {
  q: '.NET',
  location: 'Regensburg',
  employerTypes: ['Agency', 'Direct'],
  status: 'new',
  sort: 'fetched',
  page: 3,
}

describe('filtersFromQuery', () => {
  it('returns the defaults for an empty query', () => {
    expect(filtersFromQuery({})).toEqual(DEFAULT_FILTERS)
  })

  it('reads every filter from the URL', () => {
    expect(filtersFromQuery({
      q: ' .NET ',
      location: 'Regensburg',
      employerType: ['Agency', 'Direct'],
      status: 'new',
      sort: 'fetched',
      page: '3',
    })).toEqual({ ...full, employerTypes: ['Agency', 'Direct'] })
  })

  it('accepts a single employerType value', () => {
    expect(filtersFromQuery({ employerType: 'Temporary' }).employerTypes).toEqual(['Temporary'])
  })

  it('ignores unknown, duplicate and malformed values', () => {
    const filters = filtersFromQuery({
      employerType: ['Agency', 'Recruiter', 'Agency', null],
      status: 'archived',
      sort: 'title',
      page: '-2',
    })

    expect(filters.employerTypes).toEqual(['Agency'])
    expect(filters.status).toBe('all')
    expect(filters.sort).toBe('published')
    expect(filters.page).toBe(1)
  })

  it.each(['0', 'abc', '1.5x', ''])('falls back to page 1 for "%s"', page => {
    expect(filtersFromQuery({ page }).page).toBe(1)
  })

  it('uses the first value when a single-value key is repeated', () => {
    expect(filtersFromQuery({ q: ['first', 'second'] }).q).toBe('first')
  })
})

describe('filtersToQuery', () => {
  it('leaves defaults out so the default list has a clean URL', () => {
    expect(filtersToQuery({ ...DEFAULT_FILTERS, employerTypes: [] })).toEqual({})
  })

  it('writes every non-default filter', () => {
    expect(filtersToQuery(full)).toEqual({
      q: '.NET',
      location: 'Regensburg',
      employerType: ['Direct', 'Agency'],
      status: 'new',
      sort: 'fetched',
      page: '3',
    })
  })

  it('trims text and drops blank text', () => {
    expect(filtersToQuery({ ...DEFAULT_FILTERS, employerTypes: [], q: '  dev  ', location: '   ' }))
      .toEqual({ q: 'dev' })
  })

  it('round-trips through the URL', () => {
    const query = filtersToQuery(full)
    // vue-router hands query values back as strings / string arrays
    const fromUrl = filtersFromQuery(query as Record<string, string | string[]>)

    expect(fromUrl).toEqual({ ...full, employerTypes: ['Direct', 'Agency'] })
    expect(filtersToQuery(fromUrl)).toEqual(query)
  })
})

describe('filtersToApiParams', () => {
  it('maps status to the seen flag', () => {
    expect(filtersToApiParams({ ...full, status: 'new' }, 20).seen).toBe(false)
    expect(filtersToApiParams({ ...full, status: 'seen' }, 20).seen).toBe(true)
    expect(filtersToApiParams({ ...full, status: 'all' }, 20).seen).toBeUndefined()
  })

  it('passes paging, sort and the other filters', () => {
    expect(filtersToApiParams(full, 20)).toEqual({
      q: '.NET',
      location: 'Regensburg',
      employerType: ['Agency', 'Direct'],
      seen: false,
      sort: 'fetched',
      page: 3,
      pageSize: 20,
    })
  })

  it('sends only paging and sort for the defaults', () => {
    expect(filtersToApiParams({ ...DEFAULT_FILTERS, employerTypes: [] }, 20))
      .toEqual({ sort: 'published', page: 1, pageSize: 20 })
  })
})

describe('hasActiveFilters', () => {
  it('ignores sort and page', () => {
    expect(hasActiveFilters({ ...DEFAULT_FILTERS, employerTypes: [], sort: 'fetched', page: 4 })).toBe(false)
  })

  it.each<Partial<VacancyFilters>>([
    { q: 'x' },
    { location: 'Passau' },
    { employerTypes: ['Direct'] },
    { status: 'seen' },
  ])('is true for %o', patch => {
    expect(hasActiveFilters({ ...DEFAULT_FILTERS, employerTypes: [], ...patch })).toBe(true)
  })
})
