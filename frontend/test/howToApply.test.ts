import { describe, expect, it } from 'vitest'
import { howToApplyLine } from '../app/utils/howToApply'
import type { HowToApply } from '../app/types/statistics'

const base: HowToApply = {
  channel: 'Email', email: 'jobs@firma.de', contactName: 'Lisa', salutation: null, emailAllowed: true,
  portalOnly: false, requested: [], source: 'Rule', externalUrl: null,
}

describe('howToApplyLine (08a)', () => {
  it('names the address and the contact', () => {
    expect(howToApplyLine(base)).toBe('per E-Mail an jobs@firma.de (Ansprechperson: Lisa)')
  })

  it('says portal only even when an address is in the text', () => {
    expect(howToApplyLine({ ...base, portalOnly: true, channel: 'Portal', contactName: null })).toBe('nur über Portal – keine E-Mail-Bewerbung')
  })
})
