const TIME_ZONE = 'Europe/Berlin'
const DAY_MS = 24 * 60 * 60 * 1000

const relativeFormat = new Intl.RelativeTimeFormat('de', { numeric: 'auto' })
const calendarDayFormat = new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE })
const absoluteFormat = new Intl.DateTimeFormat('de-DE', { timeZone: TIME_ZONE, dateStyle: 'medium' })
const absoluteDateTimeFormat = new Intl.DateTimeFormat('de-DE', {
  timeZone: TIME_ZONE,
  dateStyle: 'medium',
  timeStyle: 'short',
})

/** Calendar day in Germany as a UTC timestamp, so that "heute"/"gestern" do not depend on the browser's zone. */
function berlinDay(date: Date): number {
  return Date.parse(`${calendarDayFormat.format(date)}T00:00:00Z`)
}

/**
 * German relative date by calendar day in Europe/Berlin: „heute", „gestern", „vor 3 Tagen", „vor 2 Wochen",
 * „vor 3 Monaten", „vor 1 Jahr". Future dates („morgen", „in 2 Tagen") are possible but unusual.
 */
export function formatRelativeDate(value: string | Date, now: Date = new Date()): string {
  const date = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  const days = Math.round((berlinDay(date) - berlinDay(now)) / DAY_MS)
  const absDays = Math.abs(days)

  if (absDays < 7) {
    return relativeFormat.format(days, 'day')
  }
  if (absDays < 30) {
    return relativeFormat.format(Math.trunc(days / 7), 'week')
  }
  if (absDays < 365) {
    return relativeFormat.format(Math.trunc(days / 30), 'month')
  }
  return relativeFormat.format(Math.trunc(days / 365), 'year')
}

const shortRelativeFormat = new Intl.RelativeTimeFormat('de', { numeric: 'always', style: 'short' })

/** For recent events: „gerade eben", „vor 5 Min.", „vor 2 Std.", then by calendar day („gestern", „vor 3 Tagen"). */
export function formatRelativeTime(value: string | Date, now: Date = new Date()): string {
  const date = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(date.getTime())) {
    return ''
  }
  const minutes = Math.round((now.getTime() - date.getTime()) / 60_000)
  if (minutes < 1) {
    return 'gerade eben'
  }
  if (minutes < 60) {
    return shortRelativeFormat.format(-minutes, 'minute')
  }
  if (minutes < 12 * 60) {
    return shortRelativeFormat.format(-Math.round(minutes / 60), 'hour')
  }
  return formatRelativeDate(date, now)
}

/** e.g. „09.10.2026" */
export function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : absoluteFormat.format(date)
}

/** e.g. „16.10.2026, 18:59" */
export function formatDateTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : absoluteDateTimeFormat.format(date)
}
