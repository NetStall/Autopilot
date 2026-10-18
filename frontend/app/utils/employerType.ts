import type { EmployerType, VacancySource } from '../types/vacancy'

const employerTypeLabels: Record<EmployerType, string> = {
  Direct: 'Direkter Arbeitgeber',
  Agency: 'Personalvermittlung',
  Temporary: 'Zeitarbeit',
  Unknown: 'Unbekannt',
}

const sourceLabels: Record<VacancySource, string> = {
  Arbeitsagentur: 'Bundesagentur für Arbeit',
  Manual: 'Manuell erfasst',
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
