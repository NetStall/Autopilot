import { describe, expect, it } from 'vitest'
import type { CvExperience, CvOptions, Letter } from '../app/types/documents'
import {
  canEditDocuments,
  cloneCvOptions,
  cloneLetter,
  hasBlockingWarnings,
  isDocumentsDirty,
  letterLength,
  moveBullet,
  moveSkillGroup,
  revisionLabel,
  toggleBullet,
  toggleSkillGroup,
  usageLabel,
  warningsForParagraph,
} from '../app/utils/documents'
import { manualStatusOptions } from '../app/utils/applications'

const letter: Letter = {
  subject: 'Bewerbung als Entwickler',
  salutation: 'Sehr geehrte Damen und Herren,',
  paragraphs: ['Absatz eins.', 'Absatz zwei.'],
  closing: 'Mit freundlichen Grüßen',
  formality: 'Sie',
  language: 'De',
  factsUsed: ['exp0.b0'],
  gapsMentioned: [],
}

const options: CvOptions = { focus: null, hiddenBullets: [], shownBullets: [], bulletOrder: [], hiddenSkillGroups: [], skillGroupOrder: [] }

const entry: CvExperience = {
  id: 'exp0',
  title: 'Dev',
  company: 'A',
  where: null,
  period: '2021 – 2025',
  bullets: [
    { id: 'exp0.b0', text: 'a', score: 3, hidden: false, autoHidden: false },
    { id: 'exp0.b1', text: 'b', score: 1, hidden: false, autoHidden: false },
    { id: 'exp0.b2', text: 'c', score: 0, hidden: true, autoHidden: true },
  ],
}

describe('letterLength', () => {
  it('counts the characters and estimates the pages', () => {
    expect(letterLength(letter)).toEqual({ characters: 101, tooLong: false, label: '101 Zeichen · voraussichtlich 1 Seite' })
    expect(letterLength({ ...letter, paragraphs: ['x'.repeat(2600)] }).tooLong).toBe(true)
  })

  it('trusts the rendered page count once known', () => {
    expect(letterLength(letter, 2)).toMatchObject({ tooLong: true, label: '101 Zeichen · 2 Seiten – zu lang' })
    expect(letterLength({ ...letter, paragraphs: ['x'.repeat(2600)] }, 1)).toMatchObject({ tooLong: false, label: '2.677 Zeichen · passt auf 1 Seite' })
  })
})

describe('warnings', () => {
  const warnings = [
    { code: 'invented-number', message: '12', paragraph: 1, severity: 'Warning' as const },
    { code: 'cliche', message: 'x', paragraph: 0, severity: 'Hint' as const },
    { code: 'missing-gap', message: 'K8s', paragraph: null, severity: 'Warning' as const },
  ]

  it('groups them by paragraph', () => {
    expect(warningsForParagraph(warnings, 1).map(w => w.code)).toEqual(['invented-number'])
    expect(warningsForParagraph(warnings, 2)).toEqual([])
  })

  it('only warnings block, hints do not', () => {
    expect(hasBlockingWarnings(warnings)).toBe(true)
    expect(hasBlockingWarnings([warnings[1]!])).toBe(false)
  })
})

describe('cv choices', () => {
  it('hides a bullet and shows it again', () => {
    const hidden = toggleBullet(options, entry.bullets[0]!)
    expect(hidden.hiddenBullets).toEqual(['exp0.b0'])
    expect(toggleBullet(hidden, { ...entry.bullets[0]!, hidden: true }).hiddenBullets).toEqual([])
  })

  it('shows an automatically hidden bullet on purpose', () => {
    expect(toggleBullet(options, entry.bullets[2]!)).toMatchObject({ shownBullets: ['exp0.b2'], hiddenBullets: [] })
    expect(toggleBullet({ ...options, shownBullets: ['exp0.b2'] }, { ...entry.bullets[2]!, hidden: false, autoHidden: false }))
      .toMatchObject({ shownBullets: [], hiddenBullets: ['exp0.b2'] })
  })

  it('moves bullets within their entry and keeps the order of other entries', () => {
    const moved = moveBullet({ ...options, bulletOrder: ['exp1.b0', 'exp1.b1'] }, entry, 'exp0.b1', -1)
    expect(moved.bulletOrder).toEqual(['exp1.b0', 'exp1.b1', 'exp0.b1', 'exp0.b0', 'exp0.b2'])
    expect(moveBullet(options, entry, 'exp0.b0', -1)).toBe(options)
    expect(moveBullet(options, entry, 'exp0.b2', 1)).toBe(options)
  })

  it('hides and orders skill groups', () => {
    const groups = [{ name: 'Backend', skills: ['C#'], hidden: false }, { name: 'Frontend', skills: ['Vue'], hidden: false }]
    expect(toggleSkillGroup(options, groups[1]!).hiddenSkillGroups).toEqual(['Frontend'])
    expect(toggleSkillGroup({ ...options, hiddenSkillGroups: ['Frontend'] }, { ...groups[1]!, hidden: true }).hiddenSkillGroups).toEqual([])
    expect(moveSkillGroup(options, groups, 'Frontend', -1).skillGroupOrder).toEqual(['Frontend', 'Backend'])
  })
})

describe('dirty state', () => {
  it('notices changes to letter or cv choices, not to copies', () => {
    const saved = { letter, cvOptions: options }
    expect(isDocumentsDirty(cloneLetter(letter), cloneCvOptions(options), saved)).toBe(false)
    expect(isDocumentsDirty({ ...letter, paragraphs: ['Neu.', 'Absatz zwei.'] }, options, saved)).toBe(true)
    expect(isDocumentsDirty(letter, { ...options, focus: 'backend' }, saved)).toBe(true)
    expect(isDocumentsDirty(letter, options, null)).toBe(false)
  })

  it('clones deeply', () => {
    const copy = cloneLetter(letter)
    copy.paragraphs.push('x')
    expect(letter.paragraphs).toHaveLength(2)
  })
})

describe('labels and rules', () => {
  it('describes revisions and usage in German', () => {
    expect(revisionLabel('Rephrased')).toBe('Absatz neu formuliert')
    expect(usageLabel(3000, 900, 0.015)).toBe('3.900 Tokens · ≈ 0,015 $')
  })

  it('documents are editable until sent', () => {
    expect(canEditDocuments('Draft')).toBe(true)
    expect(canEditDocuments('Approved')).toBe(true)
    expect(canEditDocuments('Sent')).toBe(false)
  })

  it('the generic status change never offers Entwurf, Freigegeben or Gesendet', () => {
    expect(manualStatusOptions('Draft').map(s => s.value)).toEqual(['Draft', 'Withdrawn'])
    expect(manualStatusOptions('Approved').map(s => s.value)).toEqual(['Approved', 'Withdrawn'])
    const fromSent = manualStatusOptions('Sent').map(s => s.value)
    expect(fromSent).toContain('Sent')
    expect(fromSent).toContain('Interview')
    expect(fromSent).not.toContain('Draft')
    expect(fromSent).not.toContain('Approved')
    expect(manualStatusOptions('Interview').map(s => s.value)).not.toContain('Sent')
  })
})
