import { describe, expect, it } from 'vitest'
import type { FitRequirement } from '../app/types/vacancy'
import { fitSummary, groupFit, scoreLine } from '../app/utils/fitView'

const req = (name: string, coverage: FitRequirement['coverage'], optional = false): FitRequirement =>
  ({ name, coverage, optional, note: '' })

describe('„Passt zu mir?"', () => {
  it('splits requirements into ✓ / ~ / ✗ with required before optional', () => {
    const groups = groupFit([req('Angular', 'Gap', true), req('C#', 'Covered'), req('React', 'Gap'), req('Azure', 'Partial')])

    expect(groups.covered.map(r => r.name)).toEqual(['C#'])
    expect(groups.partial.map(r => r.name)).toEqual(['Azure'])
    expect(groups.gaps.map(r => r.name)).toEqual(['React', 'Angular'])
  })

  it('summarises and counts only required gaps', () => {
    const requirements = [req('C#', 'Covered'), req('Vue', 'Covered'), req('Angular', 'Gap', true), req('WPF', 'Gap')]

    expect(fitSummary({ requirements, penalty: 6, profileStatus: 'Loaded' }))
      .toBe('2 erfüllt · 0 teilweise · 1 Lücken · Abzug 6 Punkte')
    expect(fitSummary({ requirements, penalty: 0, profileStatus: 'Missing' })).toContain('Kein gültiges Profil')
  })

  it('shows both numbers when the rank differs from the relevance', () => {
    expect(scoreLine(94, 86)).toBe('Relevanz 94 · Rang 86 (nach Abzügen aus dem Profil-Abgleich)')
    expect(scoreLine(80, 80)).toBe('Relevanz 80')
    expect(scoreLine(94, 79, 72)).toBe('Relevanz 94 · Rang 79 (nach Abzügen aus dem Profil-Abgleich) · KI 72 (nicht Teil des Rangs)')
    expect(scoreLine(null, null)).toBeNull()
  })
})
