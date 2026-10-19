import { describe, expect, it } from 'vitest'
import {
  DEFAULT_FILTERS,
  defaultFilters,
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
  remote: 'full',
  relevance: ['Relevant'],
  source: 'Arbeitsagentur',
  searches: ['remote-dotnet', 'region-csharp'],
  starredOnly: false,
  showDismissed: false,
  since: '',
  sort: 'fetched',
  page: 3,
}

const defaults = defaultFilters

describe('filtersFromQuery', () => {
  it('returns the defaults for an empty query', () => {
    expect(filtersFromQuery({})).toEqual(DEFAULT_FILTERS)
    expect(DEFAULT_FILTERS.relevance).toEqual(['Relevant', 'Maybe'])
    expect(DEFAULT_FILTERS.sort).toBe('relevance')
  })

  it('reads every filter from the URL', () => {
    expect(filtersFromQuery({
      q: ' .NET ',
      location: 'Regensburg',
      employerType: ['Agency', 'Direct'],
      status: 'new',
      remote: 'full',
      relevance: 'Relevant',
      source: 'Arbeitsagentur',
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
      relevance: ['Top', 'Maybe', 'Maybe'],
      remote: 'sometimes',
      source: 'LinkedIn',
    })

    expect(filters.employerTypes).toEqual(['Agency'])
    expect(filters.status).toBe('all')
    expect(filters.sort).toBe('relevance')
    expect(filters.page).toBe(1)
    expect(filters.relevance).toEqual(['Maybe'])
    expect(filters.remote).toBe('any')
    expect(filters.source).toBe('')
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
      remote: 'full',
      relevance: ['Relevant'],
      source: 'Arbeitsagentur',
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

describe('relevance filter', () => {
  it('defaults to Relevant + Maybe and keeps that out of the URL in any order', () => {
    expect(filtersToQuery({ ...defaults(), relevance: ['Maybe', 'Relevant'] })).toEqual({})
    expect(filtersFromQuery({ relevance: [] }).relevance).toEqual(['Relevant', 'Maybe'])
  })

  it('writes other selections in a fixed order', () => {
    expect(filtersToQuery({ ...defaults(), relevance: ['Irrelevant', 'Relevant', 'Maybe'] }))
      .toEqual({ relevance: ['Relevant', 'Maybe', 'Irrelevant'] })
    expect(filtersToQuery({ ...defaults(), relevance: ['Irrelevant'] })).toEqual({ relevance: ['Irrelevant'] })
  })

  it('sends the levels to the API, and nothing when all are selected', () => {
    expect(filtersToApiParams(defaults(), 20).relevance).toEqual(['Relevant', 'Maybe'])
    expect(filtersToApiParams({ ...defaults(), relevance: ['Maybe', 'Irrelevant', 'Relevant'] }, 20).relevance)
      .toBeUndefined()
  })

  it('counts only a non-default selection as an active filter', () => {
    expect(hasActiveFilters(defaults())).toBe(false)
    expect(hasActiveFilters({ ...defaults(), relevance: ['Relevant'] })).toBe(true)
  })
})

describe('remote, source and search filters', () => {
  it('reads the remote filter, and old remote=true links as „remote or hybrid"', () => {
    expect(filtersFromQuery({ remote: 'full' }).remote).toBe('full')
    expect(filtersFromQuery({ remote: 'remote' }).remote).toBe('remote')
    expect(filtersFromQuery({ remote: 'true' }).remote).toBe('remote')
    expect(filtersFromQuery({ remote: 'false' }).remote).toBe('any')
    expect(filtersFromQuery({}).remote).toBe('any')
  })

  it('maps the remote filter to remote levels', () => {
    expect(filtersToApiParams({ ...defaults(), remote: 'full' }, 20).remote).toEqual(['FullRemote'])
    expect(filtersToApiParams({ ...defaults(), remote: 'remote' }, 20).remote).toEqual(['FullRemote', 'Hybrid'])
    expect(filtersToApiParams(defaults(), 20).remote).toBeUndefined()
  })

  it('reads repeatable search names, trimmed and without duplicates', () => {
    expect(filtersFromQuery({ search: 'region-vue' }).searches).toEqual(['region-vue'])
    expect(filtersFromQuery({ search: [' remote-dotnet ', 'remote-dotnet', '', null] }).searches)
      .toEqual(['remote-dotnet'])
  })

  it('writes remote, source and search only when set', () => {
    expect(filtersToQuery({ ...defaults(), remote: 'full' })).toEqual({ remote: 'full' })
    expect(filtersToQuery({ ...defaults(), source: 'Manual' })).toEqual({ source: 'Manual' })
    expect(filtersToQuery({ ...defaults(), searches: ['region-vue'] })).toEqual({ search: ['region-vue'] })
  })

  it('survives a reload (URL round trip)', () => {
    const filters: VacancyFilters = { ...defaults(), remote: 'full', source: 'Arbeitsagentur', searches: ['remote-dotnet'] }
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
      remote: ['FullRemote'],
      relevance: ['Relevant'],
      source: 'Arbeitsagentur',
      search: ['remote-dotnet', 'region-csharp'],
      sort: 'fetched',
      page: 3,
      pageSize: 20,
    })
  })

  it('sends paging, sort by relevance and the default levels for the defaults', () => {
    expect(filtersToApiParams(defaults(), 20))
      .toEqual({ sort: 'relevance', relevance: ['Relevant', 'Maybe'], page: 1, pageSize: 20 })
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
    { remote: 'full' },
    { source: 'Manual' },
    { searches: ['region-vue'] },
  ])('is true for %o', patch => {
    expect(hasActiveFilters({ ...defaults(), ...patch })).toBe(true)
  })
})
