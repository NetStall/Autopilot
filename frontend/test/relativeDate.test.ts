import { describe, expect, it } from 'vitest'
import { formatDate, formatDateTime, formatRelativeDate, formatRelativeTime } from '../app/utils/relativeDate'

// 18.10.2026, 14:00 in Berlin (CEST, UTC+2)
const now = new Date('2026-10-18T12:00:00Z')

describe('formatRelativeDate', () => {
  it.each([
    ['2026-10-18T05:00:00Z', 'heute'],
    ['2026-10-17T12:00:00Z', 'gestern'],
    ['2026-10-16T12:00:00Z', 'vorgestern'],
    ['2026-10-15T12:00:00Z', 'vor 3 Tagen'],
    ['2026-10-11T12:00:00Z', 'letzte Woche'],
    ['2026-10-04T12:00:00Z', 'vor 2 Wochen'],
    ['2026-09-10T12:00:00Z', 'letzten Monat'],
    ['2026-07-01T12:00:00Z', 'vor 3 Monaten'],
    ['2025-09-01T12:00:00Z', 'letztes Jahr'],
    ['2026-10-19T08:00:00Z', 'morgen'],
  ])('%s → %s', (value, expected) => {
    expect(formatRelativeDate(value, now)).toBe(expected)
  })

  it('counts calendar days in Germany, not in the browser time zone', () => {
    // Imported date: midnight 09.10. Berlin = 08.10. 22:00 UTC. In Berlin this is 9 days before 18.10.
    expect(formatRelativeDate('2026-10-08T22:00:00Z', now)).toBe('letzte Woche')
    // 23:30 Berlin on 17.10. is still "gestern", even though it is 21:30 UTC.
    expect(formatRelativeDate('2026-10-17T21:30:00Z', now)).toBe('gestern')
    // 00:30 Berlin on 18.10. is "heute", although it is still the 17th in UTC.
    expect(formatRelativeDate('2026-10-17T22:30:00Z', now)).toBe('heute')
  })

  it('accepts Date objects', () => {
    expect(formatRelativeDate(new Date('2026-10-15T12:00:00Z'), now)).toBe('vor 3 Tagen')
  })

  it('returns an empty string for invalid input', () => {
    expect(formatRelativeDate('not a date', now)).toBe('')
  })
})

describe('formatDate / formatDateTime', () => {
  it('formats in German and in Berlin time', () => {
    expect(formatDate('2026-10-08T22:00:00Z')).toBe('09.10.2026')
    expect(formatDateTime('2026-10-16T16:59:18Z')).toBe('16.10.2026, 18:59')
  })

  it('returns an empty string for invalid input', () => {
    expect(formatDate('')).toBe('')
    expect(formatDateTime('x')).toBe('')
  })
})

describe('formatRelativeTime', () => {
  it.each([
    ['2026-10-18T11:59:40Z', 'gerade eben'],
    ['2026-10-18T11:55:00Z', 'vor 5 Min.'],
    ['2026-10-18T10:00:00Z', 'vor 2 Std.'],
    ['2026-10-18T01:00:00Z', 'vor 11 Std.'],
    ['2026-10-17T12:00:00Z', 'gestern'],
    ['2026-10-14T12:00:00Z', 'vor 4 Tagen'],
  ])('%s → %s', (value, expected) => {
    expect(formatRelativeTime(value, now)).toBe(expected)
  })
})
