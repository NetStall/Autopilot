import type { Vacancy } from '../types/vacancy'
import { employerTypeLabel, isIntermediary } from './employerType'

export type BadgeVariant = 'new' | 'remote' | 'employer' | 'intermediary'

export interface Badge {
  label: string
  variant: BadgeVariant
}

/** Badges shown for a vacancy, in display order: „Neu", „Remote", employer type. */
export function vacancyBadges(vacancy: Pick<Vacancy, 'isSeen' | 'isRemote' | 'employerType'>): Badge[] {
  const badges: Badge[] = []
  if (!vacancy.isSeen) {
    badges.push({ label: 'Neu', variant: 'new' })
  }
  if (vacancy.isRemote) {
    badges.push({ label: 'Remote', variant: 'remote' })
  }
  badges.push({
    label: employerTypeLabel(vacancy.employerType),
    variant: isIntermediary(vacancy.employerType) ? 'intermediary' : 'employer',
  })
  return badges
}
