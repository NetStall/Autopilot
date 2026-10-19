import { describe, expect, it } from 'vitest'
import { reasonKind, remoteLevelLabel, scoreBadge, vacancyBadges } from '../app/utils/vacancyBadges'

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
  it('shows the score with its level', () => {
    expect(scoreBadge({ relevanceScore: 82, relevanceLevel: 'Relevant' }))
      .toEqual({ text: '82', level: 'Relevant', description: 'Relevanz 82 von 100: Passt' })
    expect(scoreBadge({ relevanceScore: 0, relevanceLevel: 'Irrelevant' }).text).toBe('0')
  })

  it('shows a dash before scoring', () => {
    expect(scoreBadge({ relevanceScore: null, relevanceLevel: null }))
      .toEqual({ text: '–', level: 'Unscored', description: 'Noch nicht bewertet' })
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
