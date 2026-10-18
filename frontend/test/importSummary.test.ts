import { describe, expect, it } from 'vitest'
import { formatImportSummary, formatSearchSummary } from '../app/utils/importSummary'
import { describeApiError, errorStatus } from '../app/utils/apiError'

describe('formatImportSummary', () => {
  const result = {
    fetched: 260,
    added: 85,
    updated: 12,
    unchanged: 140,
    excluded: 23,
    durationMs: 2100,
    searches: [],
  }

  it('lists new, updated, unchanged and excluded vacancies', () => {
    expect(formatImportSummary(result)).toBe('85 neu · 12 aktualisiert · 140 unverändert · 23 ausgeschlossen')
  })

  it('shows zeros instead of hiding them', () => {
    expect(formatImportSummary({ ...result, added: 0, updated: 0, unchanged: 0, excluded: 0 }))
      .toBe('0 neu · 0 aktualisiert · 0 unverändert · 0 ausgeschlossen')
  })

  it('formats one line of the per-search breakdown', () => {
    expect(formatSearchSummary({
      name: 'remote-dotnet',
      keyword: '.NET',
      location: null,
      remoteOnly: true,
      fetched: 3,
      added: 1,
      updated: 0,
      unchanged: 2,
      excluded: 0,
    })).toBe('remote-dotnet: 3 gefunden — 1 neu · 0 aktualisiert · 2 unverändert · 0 ausgeschlossen')
  })
})

describe('describeApiError', () => {
  it('uses the ProblemDetails title and detail (e.g. 502 from the import)', () => {
    const error = {
      statusCode: 502,
      data: {
        status: 502,
        title: 'The Arbeitsagentur Jobsuche API is not reachable.',
        detail: 'Nothing was imported.',
      },
    }

    expect(describeApiError(error)).toBe('The Arbeitsagentur Jobsuche API is not reachable. Nothing was imported.')
    expect(errorStatus(error)).toBe(502)
  })

  it('falls back to the status code without a problem body', () => {
    expect(describeApiError({ statusCode: 500, data: '<html>' })).toBe('Fehler 500.')
  })

  it('reports an unreachable backend when there is no response', () => {
    expect(describeApiError({ message: 'fetch failed' })).toBe('Das Backend ist nicht erreichbar.')
    expect(describeApiError({ statusCode: 0 })).toBe('Das Backend ist nicht erreichbar.')
  })

  it('handles non-objects', () => {
    expect(describeApiError(null)).toBe('Unbekannter Fehler.')
    expect(describeApiError('boom')).toBe('Unbekannter Fehler.')
    expect(errorStatus(undefined)).toBeUndefined()
  })
})
