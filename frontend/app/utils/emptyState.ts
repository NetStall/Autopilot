import { hasActiveFilters, type VacancyFilters } from './vacancyFilters'

export interface EmptyState {
  title: string
  hint: string
  /** Offer „Filter zurücksetzen". */
  canReset: boolean
}

/** Text for an empty list, specific to the filter that is most likely responsible. */
export function emptyStateFor(filters: VacancyFilters): EmptyState {
  if (filters.starredOnly) {
    return {
      title: 'Noch nichts gemerkt.',
      hint: 'Mit „☆ Merken" oder der Taste m merkst du dir interessante Stellen.',
      canReset: true,
    }
  }
  if (filters.since) {
    return {
      title: 'Seit dem letzten Import keine neuen Stellen.',
      hint: 'Entferne den Filter „Neu seit letztem Import", um alle Stellen zu sehen.',
      canReset: true,
    }
  }
  if (hasActiveFilters(filters)) {
    return { title: 'Keine Stellen gefunden.', hint: 'Passe die Filter an oder setze sie zurück.', canReset: true }
  }
  return {
    title: 'Keine Stellen gefunden.',
    hint: 'Starte oben einen Import, um Stellen der Bundesagentur für Arbeit zu laden.',
    canReset: false,
  }
}
