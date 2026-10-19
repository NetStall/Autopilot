import { describe, expect, it } from 'vitest'
import { vacancyBadges } from '../app/utils/vacancyBadges'

describe('vacancyBadges', () => {
  it('shows Neu, Remote and the employer type in this order', () => {
    expect(vacancyBadges({ isSeen: false, isRemote: true, employerType: 'Direct' })).toEqual([
      { label: 'Neu', variant: 'new' },
      { label: 'Remote', variant: 'remote' },
      { label: 'Direkter Arbeitgeber', variant: 'employer' },
    ])
  })

  it('shows no Remote badge for regional vacancies', () => {
    const badges = vacancyBadges({ isSeen: true, isRemote: false, employerType: 'Unknown' })

    expect(badges.map(b => b.label)).toEqual(['Unbekannt'])
  })

  it('marks agencies and temporary work as intermediaries', () => {
    expect(vacancyBadges({ isSeen: true, isRemote: true, employerType: 'Agency' })).toEqual([
      { label: 'Remote', variant: 'remote' },
      { label: 'Personalvermittlung', variant: 'intermediary' },
    ])
    expect(vacancyBadges({ isSeen: true, isRemote: false, employerType: 'Temporary' })[0])
      .toEqual({ label: 'Zeitarbeit', variant: 'intermediary' })
  })

  it('shows Gemerkt and Ausgeblendet', () => {
    expect(vacancyBadges({ isSeen: true, isRemote: true, employerType: 'Direct', interest: 'Starred' }).map(b => b.variant))
      .toEqual(['starred', 'remote', 'employer'])
    expect(vacancyBadges({ isSeen: false, isRemote: false, employerType: 'Direct', interest: 'Dismissed' }).map(b => b.label))
      .toEqual(['Neu', 'Ausgeblendet', 'Direkter Arbeitgeber'])
  })
})
