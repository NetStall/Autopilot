import type { EmployerType, VacancySource } from '../types/vacancy'

const employerTypeLabels: Record<EmployerType, string> = {
  Direct: 'Direkter Arbeitgeber',
  Agency: 'Personalvermittlung',
  Temporary: 'Zeitarbeit',
  Unknown: 'Unbekannt',
}

const sourceLabels: Record<VacancySource, string> = {
  Arbeitsagentur: 'Arbeitsagentur',
  Himalayas: 'Himalayas',
  Manual: 'Manuell erfasst',
}

export interface SourceAttribution {
  /** e.g. „via Himalayas" */
  label: string
  /** The source's own site — the Himalayas terms require a visible link. */
  href: string
}

const attributions: Partial<Record<VacancySource, SourceAttribution>> = {
  Himalayas: { label: 'via Himalayas', href: 'https://himalayas.app' },
  Arbeitsagentur: { label: 'via Bundesagentur für Arbeit', href: 'https://www.arbeitsagentur.de/jobsuche/' },
}

/** Credit and link for vacancies from an external source; null for manual ones. */
export function sourceAttribution(source: VacancySource): SourceAttribution | null {
  return attributions[source] ?? null
}

/** German UI label for an employer type. */
export function employerTypeLabel(type: EmployerType): string {
  return employerTypeLabels[type]
}

/** Agencies and temporary work are shown, but marked so they stand out from direct employers. */
export function isIntermediary(type: EmployerType): boolean {
  return type === 'Agency' || type === 'Temporary'
}

/** German UI label for a vacancy source. */
export function sourceLabel(source: VacancySource): string {
  return sourceLabels[source]
}
