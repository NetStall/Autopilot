import type { Vacancy } from '../types/vacancy'
import { employerTypeLabel, isIntermediary } from './employerType'

export type BadgeVariant = 'new' | 'remote' | 'starred' | 'dismissed' | 'employer' | 'intermediary'

export interface Badge {
  label: string
  variant: BadgeVariant
}

/** Badges shown for a vacancy, in display order: „Neu", „Gemerkt"/„Ausgeblendet", „Remote", employer type. */
export function vacancyBadges(
  vacancy: Pick<Vacancy, 'isSeen' | 'isRemote' | 'employerType'> & Partial<Pick<Vacancy, 'interest'>>,
): Badge[] {
  const badges: Badge[] = []
  if (!vacancy.isSeen) {
    badges.push({ label: 'Neu', variant: 'new' })
  }
  if (vacancy.interest === 'Starred') {
    badges.push({ label: '★ Gemerkt', variant: 'starred' })
  }
  else if (vacancy.interest === 'Dismissed') {
    badges.push({ label: 'Ausgeblendet', variant: 'dismissed' })
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
