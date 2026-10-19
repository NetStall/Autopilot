import type { FitRequirement, FitResponse } from '../types/vacancy'

export interface FitGroups {
  /** ✓ used professionally */
  covered: FitRequirement[]
  /** ~ own projects / learning */
  partial: FitRequirement[]
  /** ✗ not in the profile or a known gap */
  gaps: FitRequirement[]
}

/** Requirements split into the three groups of „Passt zu mir?"; required ones before optional ones. */
export function groupFit(requirements: readonly FitRequirement[]): FitGroups {
  const byOptional = (a: FitRequirement, b: FitRequirement) => Number(a.optional) - Number(b.optional)
  return {
    covered: requirements.filter(r => r.coverage === 'Covered').sort(byOptional),
    partial: requirements.filter(r => r.coverage === 'Partial').sort(byOptional),
    gaps: requirements.filter(r => r.coverage === 'Gap').sort(byOptional),
  }
}

/** One-line verdict for the section header. */
export function fitSummary(fit: Pick<FitResponse, 'requirements' | 'penalty' | 'profileStatus'>): string {
  if (fit.profileStatus !== 'Loaded') {
    return 'Kein gültiges Profil geladen – siehe Seite „Profil".'
  }
  const groups = groupFit(fit.requirements)
  const requiredGaps = groups.gaps.filter(g => !g.optional).length
  const parts = [`${groups.covered.length} erfüllt`, `${groups.partial.length} teilweise`, `${requiredGaps} Lücken`]
  return fit.penalty > 0 ? `${parts.join(' · ')} · Abzug ${fit.penalty} Punkte` : parts.join(' · ')
}

/** „Relevanz 94 · Rang 86" — both numbers for the „Warum?" panel. */
export function scoreLine(relevanceScore: number | null, rankScore: number | null): string | null {
  if (relevanceScore == null) {
    return null
  }
  return rankScore != null && rankScore !== relevanceScore
    ? `Relevanz ${relevanceScore} · Rang ${rankScore} (nach Abzügen aus dem Profil-Abgleich)`
    : `Relevanz ${relevanceScore}`
}
