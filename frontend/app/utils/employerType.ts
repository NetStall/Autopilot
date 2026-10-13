import type { EmployerType } from '../types/vacancy'

const labels: Record<EmployerType, string> = {
  Direct: 'Direkter Arbeitgeber',
  Agency: 'Personalvermittlung',
  Temporary: 'Zeitarbeit',
  Unknown: 'Unbekannt',
}

/** German UI label for an employer type. */
export function employerTypeLabel(type: EmployerType): string {
  return labels[type]
}

/** Agencies and temporary work are shown, but marked so they stand out from direct employers. */
export function isIntermediary(type: EmployerType): boolean {
  return type === 'Agency' || type === 'Temporary'
}
