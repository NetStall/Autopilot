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
  remoteOnly: true,
  searches: ['remote-dotnet', 'region-csharp'],
  starredOnly: false,
  showDismissed: false,
  since: '',
  sort: 'fetched',
  page: 3,
}

const defaults = (): VacancyFilters => ({ ...DEFAULT_FILTERS, employerTypes: [], searches: [] })

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
      remote: 'true',
      search: ['remote-dotnet', 'region-csharp'],
      sort: 'fetched',
      page: '3',
    })).toEqual(full)
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
    expect(filtersToQuery(defaults())).toEqual({})
  })

  it('writes every non-default filter', () => {
    expect(filtersToQuery(full)).toEqual({
      q: '.NET',
      location: 'Regensburg',
      employerType: ['Direct', 'Agency'],
      status: 'new',
      remote: 'true',
      search: ['remote-dotnet', 'region-csharp'],
      sort: 'fetched',
      page: '3',
    })
  })

  it('trims text and drops blank text', () => {
    expect(filtersToQuery({ ...defaults(), q: '  dev  ', location: '   ' }))
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

describe('remote and search filters', () => {
  it('reads remote only from remote=true', () => {
    expect(filtersFromQuery({ remote: 'true' }).remoteOnly).toBe(true)
    expect(filtersFromQuery({ remote: 'false' }).remoteOnly).toBe(false)
    expect(filtersFromQuery({ remote: 'yes' }).remoteOnly).toBe(false)
    expect(filtersFromQuery({}).remoteOnly).toBe(false)
  })

  it('reads repeatable search names, trimmed and without duplicates', () => {
    expect(filtersFromQuery({ search: 'region-vue' }).searches).toEqual(['region-vue'])
    expect(filtersFromQuery({ search: [' remote-dotnet ', 'remote-dotnet', '', null] }).searches)
      .toEqual(['remote-dotnet'])
  })

  it('writes remote and search only when set', () => {
    expect(filtersToQuery({ ...defaults(), remoteOnly: true })).toEqual({ remote: 'true' })
    expect(filtersToQuery({ ...defaults(), searches: ['region-vue'] })).toEqual({ search: ['region-vue'] })
    expect(filtersToQuery(defaults())).toEqual({})
  })

  it('maps to the API parameters remote and search', () => {
    const params = filtersToApiParams({ ...defaults(), remoteOnly: true, searches: ['remote-vue'] }, 20)

    expect(params.remote).toBe(true)
    expect(params.search).toEqual(['remote-vue'])
    expect(filtersToApiParams(defaults(), 20).remote).toBeUndefined()
  })

  it('survives a reload (URL round trip)', () => {
    const filters = { ...defaults(), remoteOnly: true, searches: ['remote-dotnet', 'region-vue'] }
    const fromUrl = filtersFromQuery(filtersToQuery(filters) as Record<string, string | string[]>)

    expect(fromUrl).toEqual(filters)
  })
})

describe('interest and since filters', () => {
  it('reads and writes Gemerkt, Ausgeblendete anzeigen and since', () => {
    const query = { starred: 'true', dismissed: 'show', since: '2026-10-19T05:00:12.345Z' }
    const filters = filtersFromQuery(query)

    expect(filters).toMatchObject({ starredOnly: true, showDismissed: true, since: '2026-10-19T05:00:12.345Z' })
    expect(filtersToQuery(filters)).toEqual(query)
  })

  it('ignores a malformed since value', () => {
    expect(filtersFromQuery({ since: 'yesterday' }).since).toBe('')
    expect(filtersFromQuery({ since: '2026-13-45T99:00:00Z' }).since).toBe('')
  })

  it('maps to interest / includeDismissed / fetchedSince', () => {
    expect(filtersToApiParams({ ...defaults(), starredOnly: true, showDismissed: true }, 20))
      .toMatchObject({ interest: ['Starred'] })
    expect(filtersToApiParams({ ...defaults(), starredOnly: true }, 20).includeDismissed).toBeUndefined()
    expect(filtersToApiParams({ ...defaults(), showDismissed: true }, 20).includeDismissed).toBe(true)
    expect(filtersToApiParams({ ...defaults(), since: '2026-10-19T05:00:00Z' }, 20).fetchedSince)
      .toBe('2026-10-19T05:00:00Z')
    expect(filtersToApiParams(defaults(), 20)).not.toHaveProperty('interest')
  })

  it.each<Partial<VacancyFilters>>([{ starredOnly: true }, { showDismissed: true }, { since: '2026-10-19T05:00:00Z' }])(
    'counts %o as an active filter',
    (patch) => {
      expect(hasActiveFilters({ ...defaults(), ...patch })).toBe(true)
    },
  )
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
      remote: true,
      search: ['remote-dotnet', 'region-csharp'],
      sort: 'fetched',
      page: 3,
      pageSize: 20,
    })
  })

  it('sends only paging and sort for the defaults', () => {
    expect(filtersToApiParams(defaults(), 20))
      .toEqual({ sort: 'published', page: 1, pageSize: 20 })
  })
})

describe('hasActiveFilters', () => {
  it('ignores sort and page', () => {
    expect(hasActiveFilters({ ...defaults(), sort: 'fetched', page: 4 })).toBe(false)
  })

  it.each<Partial<VacancyFilters>>([
    { q: 'x' },
    { location: 'Passau' },
    { employerTypes: ['Direct'] },
    { status: 'seen' },
    { remoteOnly: true },
    { searches: ['region-vue'] },
  ])('is true for %o', patch => {
    expect(hasActiveFilters({ ...defaults(), ...patch })).toBe(true)
  })
})
