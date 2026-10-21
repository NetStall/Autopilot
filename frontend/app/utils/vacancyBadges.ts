import type { RelevanceLevel, RemoteLevel, Vacancy } from '../types/vacancy'
import { employerTypeLabel, isIntermediary, sourceLabel } from './employerType'

export type BadgeVariant = 'new' | 'remote' | 'hybrid' | 'starred' | 'dismissed' | 'employer' | 'intermediary'

export interface Badge {
  label: string
  variant: BadgeVariant
  /** Tooltip, e.g. where the remote level comes from („Homeoffice: teilweise (laut Text)"). */
  title?: string
}

const remoteBadges: Partial<Record<RemoteLevel, Badge>> = {
  FullRemote: { label: 'Remote', variant: 'remote' },
  Hybrid: { label: 'Hybrid', variant: 'hybrid' },
}

/** Badges shown for a vacancy, in display order: „Neu", „Gemerkt"/„Ausgeblendet", „Remote"/„Hybrid", employer type. */
export function vacancyBadges(
  vacancy: Pick<Vacancy, 'isSeen' | 'remoteLevel' | 'employerType'> & Partial<Pick<Vacancy, 'interest' | 'remoteNote'>>,
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
    const partial = vacancy.remoteLevel === 'Hybrid' && vacancy.remoteNote?.includes('teilweise')
    badges.push({ ...remote, ...(partial ? { label: 'Hybrid (teilweise)' } : {}), ...(vacancy.remoteNote ? { title: vacancy.remoteNote } : {}) })
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

export type ScoreTone = 'high' | 'medium' | 'low' | 'unscored'

/** Colour thresholds of the score badge (Ui:ScoreHigh / Ui:ScoreMedium). */
export interface ScoreThresholds {
  high: number
  medium: number
}

export const DEFAULT_SCORE_THRESHOLDS: Readonly<ScoreThresholds> = Object.freeze({ high: 70, medium: 45 })

export interface ScoreBadge {
  /** The rank score (relevance minus fit penalties), e.g. „87", or „–" while not scored. Always shown. */
  text: string
  tone: ScoreTone
  level: RelevanceLevel | 'Unscored'
  /** For the aria-label, e.g. „Bewertung 87 von 100". */
  description: string
  /** Second line / tooltip: „Relevanz 94 · Abzüge −8". */
  detail: string
}

/**
 * The score badge of a vacancy: the rank score as the main number (falls back to the relevance score), coloured by
 * the thresholds — but never by colour alone: the number is always visible and part of the label.
 */
export function scoreBadge(
  vacancy: Pick<Vacancy, 'relevanceScore' | 'relevanceLevel'> & Partial<Pick<Vacancy, 'rankScore'>>,
  thresholds: ScoreThresholds = DEFAULT_SCORE_THRESHOLDS,
): ScoreBadge {
  if (vacancy.relevanceScore == null || vacancy.relevanceLevel == null) {
    return { text: '–', tone: 'unscored', level: 'Unscored', description: 'Noch nicht bewertet', detail: 'Noch nicht bewertet' }
  }
  const rank = vacancy.rankScore ?? vacancy.relevanceScore
  const penalty = vacancy.relevanceScore - rank
  return {
    text: String(rank),
    tone: rank >= thresholds.high ? 'high' : rank >= thresholds.medium ? 'medium' : 'low',
    level: vacancy.relevanceLevel,
    description: `Bewertung ${rank} von 100`,
    detail: penalty > 0
      ? `Relevanz ${vacancy.relevanceScore} · Abzüge −${penalty}`
      : `Relevanz ${vacancy.relevanceScore} · keine Abzüge`,
  }
}

/** „Arbeitsagentur", or for merged duplicates all sources: „Arbeitsagentur + Himalayas". */
export function sourcesLine(vacancy: Pick<Vacancy, 'source'> & Partial<Pick<Vacancy, 'alsoFoundAt'>>): string {
  const sources = [vacancy.source, ...(vacancy.alsoFoundAt ?? []).map(l => l.source)]
  return [...new Set(sources)].map(sourceLabel).join(' + ')
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
