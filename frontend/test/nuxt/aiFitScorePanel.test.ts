import { describe, expect, it } from 'vitest'
import { mountSuspended } from '@nuxt/test-utils/runtime'
import AiFitScorePanel from '~/components/AiFitScorePanel.vue'

describe('AiFitScorePanel', () => {
  it('shows score, verdict, reasons and blockers', async () => {
    const wrapper = await mountSuspended(AiFitScorePanel, {
      props: { score: { score: 41, verdict: 'eher nicht', reasons: ['C# passt.', 'AWS fehlt.', 'Remote passt.'], blockers: ['5 Jahre AWS'] } },
    })

    expect(wrapper.find('.ai__head').text()).toContain('KI-Bewertung: 41 – eher nicht')
    expect(wrapper.findAll('.ai__reasons li')).toHaveLength(3)
    expect(wrapper.find('.ai__blockers').text()).toBe('Ausschlussgründe: 5 Jahre AWS')
  })
})
