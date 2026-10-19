import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import type { Application, ApplicationStatus } from '../types/application'

/** All statuses in the order of the AGENTS.md vocabulary, with the German UI labels. */
export const APPLICATION_STATUSES: readonly { value: ApplicationStatus, label: string }[] = [
  { value: 'Draft', label: 'Entwurf' },
  { value: 'Approved', label: 'Freigegeben' },
  { value: 'Sent', label: 'Gesendet' },
  { value: 'Acknowledged', label: 'Eingangsbestätigung' },
  { value: 'Response', label: 'Rückmeldung' },
  { value: 'Interview', label: 'Interview' },
  { value: 'TechInterview', label: 'Tech-Interview' },
  { value: 'Offer', label: 'Angebot' },
  { value: 'Rejected', label: 'Absage' },
  { value: 'NoResponse', label: 'Keine Antwort' },
  { value: 'Withdrawn', label: 'Zurückgezogen' },
]

export type StatusTone = 'open' | 'progress' | 'positive' | 'closed'

/** Colour group of a status badge. */
export function statusTone(status: ApplicationStatus): StatusTone {
  switch (status) {
    case 'Sent':
    case 'Acknowledged':
    case 'Response':
      return 'open'
    case 'Interview':
    case 'TechInterview':
      return 'progress'
    case 'Offer':
      return 'positive'
    case 'Draft':
    case 'Approved':
      return 'open'
    default:
      return 'closed'
  }
}

export function statusLabel(status: ApplicationStatus): string {
  return APPLICATION_STATUSES.find(s => s.value === status)?.label ?? status
}

/** Filters of the „Bewerbungen" page, kept in the URL. */
export interface ApplicationFilters {
  status: ApplicationStatus | ''
  channel: string
  followUpDue: boolean
}

export function applicationFiltersFromQuery(query: LocationQuery): ApplicationFilters {
  const first = (value: LocationQuery[string] | undefined) => (Array.isArray(value) ? value[0] : value) ?? ''
  const status = String(first(query.status))
  return {
    status: APPLICATION_STATUSES.some(s => s.value === status) ? status as ApplicationStatus : '',
    channel: String(first(query.channel)).trim().slice(0, 100),
    followUpDue: first(query.followUp) === 'due',
  }
}

export function applicationFiltersToQuery(filters: ApplicationFilters): LocationQueryRaw {
  const query: LocationQueryRaw = {}
  if (filters.status) {
    query.status = filters.status
  }
  if (filters.channel) {
    query.channel = filters.channel
  }
  if (filters.followUpDue) {
    query.followUp = 'due'
  }
  return query
}

/** Channels present in the list, for the filter select. */
export function channelsOf(applications: readonly Pick<Application, 'channel'>[]): string[] {
  return [...new Set(applications.map(a => a.channel).filter((c): c is string => !!c))].sort((a, b) => a.localeCompare(b, 'de'))
}

/** „Follow-up fällig" first, then newest application. */
export function sortForTable<T extends Pick<Application, 'isFollowUpDue' | 'appliedAt'>>(applications: readonly T[]): T[] {
  return [...applications].sort((a, b) =>
    Number(b.isFollowUpDue) - Number(a.isFollowUpDue) || b.appliedAt.localeCompare(a.appliedAt))
}
