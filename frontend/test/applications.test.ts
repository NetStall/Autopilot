import { describe, expect, it } from 'vitest'
import {
  APPLICATION_STATUSES,
  applicationFiltersFromQuery,
  applicationFiltersToQuery,
  channelsOf,
  sortForTable,
  statusLabel,
  statusTone,
} from '../app/utils/applications'

describe('application statuses', () => {
  it('has the eleven German labels of AGENTS.md', () => {
    expect(APPLICATION_STATUSES.map(s => s.label)).toEqual([
      'Entwurf', 'Freigegeben', 'Gesendet', 'Eingangsbestätigung', 'Rückmeldung', 'Interview', 'Tech-Interview',
      'Angebot', 'Absage', 'Keine Antwort', 'Zurückgezogen',
    ])
    expect(statusLabel('NoResponse')).toBe('Keine Antwort')
  })

  it('groups statuses by tone', () => {
    expect(statusTone('Sent')).toBe('open')
    expect(statusTone('TechInterview')).toBe('progress')
    expect(statusTone('Offer')).toBe('positive')
    expect(statusTone('Rejected')).toBe('closed')
  })
})

describe('„Bewerbungen" filters in the URL', () => {
  it('round-trips and leaves defaults out', () => {
    const filters = { status: 'Rejected' as const, channel: 'LinkedIn', followUpDue: true }
    const query = applicationFiltersToQuery(filters)

    expect(query).toEqual({ status: 'Rejected', channel: 'LinkedIn', followUp: 'due' })
    expect(applicationFiltersFromQuery(query as Record<string, string>)).toEqual(filters)
    expect(applicationFiltersToQuery({ status: '', channel: '', followUpDue: false })).toEqual({})
  })

  it('ignores unknown statuses', () => {
    expect(applicationFiltersFromQuery({ status: 'Ghosted' }).status).toBe('')
  })
})

describe('table', () => {
  it('shows follow-up due first, then the newest', () => {
    const rows = sortForTable([
      { id: 'a', isFollowUpDue: false, appliedAt: '2026-10-18T10:00:00Z' },
      { id: 'b', isFollowUpDue: true, appliedAt: '2026-10-12T10:00:00Z' },
      { id: 'c', isFollowUpDue: false, appliedAt: '2026-10-19T10:00:00Z' },
    ])

    expect(rows.map(r => r.id)).toEqual(['b', 'c', 'a'])
  })

  it('lists the channels once, sorted', () => {
    expect(channelsOf([{ channel: 'Stepstone' }, { channel: null }, { channel: 'Arbeitsagentur' }, { channel: 'Stepstone' }]))
      .toEqual(['Arbeitsagentur', 'Stepstone'])
  })
})
