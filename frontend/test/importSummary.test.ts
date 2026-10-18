import { describe, expect, it } from 'vitest'
import { formatImportSummary } from '../app/utils/importSummary'
import { describeApiError, errorStatus } from '../app/utils/apiError'

describe('formatImportSummary', () => {
  it('lists new, updated and unchanged vacancies', () => {
    expect(formatImportSummary({ fetched: 55, added: 12, updated: 3, unchanged: 40, durationMs: 2100 }))
      .toBe('12 neu · 3 aktualisiert · 40 unverändert')
  })

  it('shows zeros instead of hiding them', () => {
    expect(formatImportSummary({ fetched: 0, added: 0, updated: 0, unchanged: 0, durationMs: 5 }))
      .toBe('0 neu · 0 aktualisiert · 0 unverändert')
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
