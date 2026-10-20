import { describe, expect, it } from 'vitest'
import { reasonKind, remoteLevelLabel, scoreBadge, sourcesLine, vacancyBadges } from '../app/utils/vacancyBadges'

describe('vacancyBadges', () => {
  it('shows Neu, Remote and the employer type in this order', () => {
    expect(vacancyBadges({ isSeen: false, remoteLevel: 'FullRemote', employerType: 'Direct' })).toEqual([
      { label: 'Neu', variant: 'new' },
      { label: 'Remote', variant: 'remote' },
      { label: 'Direkter Arbeitgeber', variant: 'employer' },
    ])
  })

  it('shows Hybrid, and nothing for on-site or unknown', () => {
    expect(vacancyBadges({ isSeen: true, remoteLevel: 'Hybrid', employerType: 'Unknown' }).map(b => b.label))
      .toEqual(['Hybrid', 'Unbekannt'])
    expect(vacancyBadges({ isSeen: true, remoteLevel: 'OnSite', employerType: 'Unknown' }).map(b => b.label))
      .toEqual(['Unbekannt'])
    expect(vacancyBadges({ isSeen: true, remoteLevel: 'Unknown', employerType: 'Unknown' }).map(b => b.label))
      .toEqual(['Unbekannt'])
  })

  it('marks agencies and temporary work as intermediaries', () => {
    expect(vacancyBadges({ isSeen: true, remoteLevel: 'FullRemote', employerType: 'Agency' })).toEqual([
      { label: 'Remote', variant: 'remote' },
      { label: 'Personalvermittlung', variant: 'intermediary' },
    ])
    expect(vacancyBadges({ isSeen: true, remoteLevel: 'Unknown', employerType: 'Temporary' })[0])
      .toEqual({ label: 'Zeitarbeit', variant: 'intermediary' })
  })

  it('shows Gemerkt and Ausgeblendet', () => {
    expect(vacancyBadges({ isSeen: true, remoteLevel: 'FullRemote', employerType: 'Direct', interest: 'Starred' }).map(b => b.variant))
      .toEqual(['starred', 'remote', 'employer'])
    expect(vacancyBadges({ isSeen: false, remoteLevel: 'Unknown', employerType: 'Direct', interest: 'Dismissed' }).map(b => b.label))
      .toEqual(['Neu', 'Ausgeblendet', 'Direkter Arbeitgeber'])
  })
})

describe('scoreBadge', () => {
  it('shows the rank score with relevance and penalty', () => {
    expect(scoreBadge({ relevanceScore: 94, rankScore: 86, relevanceLevel: 'Relevant' })).toEqual({
      text: '86',
      tone: 'high',
      level: 'Relevant',
      description: 'Bewertung 86 von 100',
      detail: 'Relevanz 94 · Abzüge −8',
    })
  })

  it('falls back to the relevance score and says when nothing was deducted', () => {
    const badge = scoreBadge({ relevanceScore: 50, relevanceLevel: 'Maybe' })
    expect(badge).toMatchObject({ text: '50', tone: 'medium', detail: 'Relevanz 50 · keine Abzüge' })
  })

  it.each([
    [70, 'high'],
    [69, 'medium'],
    [45, 'medium'],
    [44, 'low'],
    [0, 'low'],
  ])('colours %i as %s with the default thresholds', (rank, tone) => {
    expect(scoreBadge({ relevanceScore: rank, rankScore: rank, relevanceLevel: 'Maybe' }).tone).toBe(tone)
  })

  it('uses configured thresholds', () => {
    expect(scoreBadge({ relevanceScore: 60, relevanceLevel: 'Maybe' }, { high: 60, medium: 30 }).tone).toBe('high')
  })

  it('shows a dash before scoring', () => {
    expect(scoreBadge({ relevanceScore: null, relevanceLevel: null }))
      .toMatchObject({ text: '–', tone: 'unscored', level: 'Unscored', description: 'Noch nicht bewertet' })
  })
})

describe('sourcesLine', () => {
  it('lists the source and the merged duplicates once each', () => {
    expect(sourcesLine({ source: 'Arbeitsagentur' })).toBe('Arbeitsagentur')
    expect(sourcesLine({
      source: 'Arbeitsagentur',
      alsoFoundAt: [{ source: 'Himalayas' }, { source: 'Himalayas' }] as never,
    })).toBe('Arbeitsagentur + Himalayas')
  })
})

describe('reasons', () => {
  it.each([
    ['C# im Titel ✓ (+50)', 'positive'],
    ['Java als Hauptsprache im Titel ✗ (-60)', 'negative'],
    ['Java neben C#/.NET im Titel – abgeschwächt (-15)', 'softened'],
    ['Voll remote: „100 % remote“', 'info'],
    ['Nur der Titel bewertet (noch keine Beschreibung)', 'info'],
  ])('classifies "%s" as %s', (reason, kind) => {
    expect(reasonKind(reason)).toBe(kind)
  })

  it('labels remote levels in German', () => {
    expect(remoteLevelLabel('FullRemote')).toBe('Voll remote')
    expect(remoteLevelLabel('OnSite')).toBe('Vor Ort (kein Homeoffice)')
  })
})
