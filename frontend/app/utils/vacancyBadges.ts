import type { RelevanceLevel, RemoteLevel, Vacancy } from '../types/vacancy'
import { employerTypeLabel, isIntermediary } from './employerType'

export type BadgeVariant = 'new' | 'remote' | 'hybrid' | 'starred' | 'dismissed' | 'employer' | 'intermediary'

export interface Badge {
  label: string
  variant: BadgeVariant
}

const remoteBadges: Partial<Record<RemoteLevel, Badge>> = {
  FullRemote: { label: 'Remote', variant: 'remote' },
  Hybrid: { label: 'Hybrid', variant: 'hybrid' },
}

/** Badges shown for a vacancy, in display order: „Neu", „Gemerkt"/„Ausgeblendet", „Remote"/„Hybrid", employer type. */
export function vacancyBadges(
  vacancy: Pick<Vacancy, 'isSeen' | 'remoteLevel' | 'employerType'> & Partial<Pick<Vacancy, 'interest'>>,
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
  const remote = remoteBadges[vacancy.remoteLevel]
  if (remote) {
    badges.push({ ...remote })
  }
  badges.push({
    label: employerTypeLabel(vacancy.employerType),
    variant: isIntermediary(vacancy.employerType) ? 'intermediary' : 'employer',
  })
  return badges
}

const relevanceLabels: Record<RelevanceLevel, string> = {
  Relevant: 'Passt',
  Maybe: 'Vielleicht',
  Irrelevant: 'Passt nicht',
}

/** German label of a relevance level. */
export function relevanceLabel(level: RelevanceLevel): string {
  return relevanceLabels[level]
}

export interface ScoreBadge {
  /** e.g. „82" or „–" while not scored. */
  text: string
  level: RelevanceLevel | 'Unscored'
  /** Accessible description, e.g. „Relevanz 82 von 100: Passt". */
  description: string
}

/** The score badge of a card; a vacancy that is not scored yet shows „–". */
export function scoreBadge(vacancy: Pick<Vacancy, 'relevanceScore' | 'relevanceLevel'>): ScoreBadge {
  if (vacancy.relevanceScore == null || vacancy.relevanceLevel == null) {
    return { text: '–', level: 'Unscored', description: 'Noch nicht bewertet' }
  }
  return {
    text: String(vacancy.relevanceScore),
    level: vacancy.relevanceLevel,
    description: `Relevanz ${vacancy.relevanceScore} von 100: ${relevanceLabel(vacancy.relevanceLevel)}`,
  }
}

const remoteLevelLabels: Record<RemoteLevel, string> = {
  FullRemote: 'Voll remote',
  Hybrid: 'Hybrid / Homeoffice möglich',
  OnSite: 'Vor Ort (kein Homeoffice)',
  Unknown: 'Nicht bekannt',
}

/** German label of a remote level (detail page). */
export function remoteLevelLabel(level: RemoteLevel): string {
  return remoteLevelLabels[level]
}

export type ReasonKind = 'positive' | 'negative' | 'softened' | 'info'

/** Classifies a reason from the backend by its marker, for colouring in the „Warum?" panel. */
export function reasonKind(reason: string): ReasonKind {
  if (reason.includes('abgeschwächt')) {
    return 'softened'
  }
  if (reason.includes('✓')) {
    return 'positive'
  }
  if (reason.includes('✗')) {
    return 'negative'
  }
  return 'info'
}
