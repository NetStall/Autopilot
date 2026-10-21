import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import ChannelPanel from '~/components/ChannelPanel.vue'

const api = vi.hoisted(() => ({ channelStatistics: vi.fn() }))
mockNuxtImport('useVacanciesApi', () => () => api)

describe('ChannelPanel (08a)', () => {
  it('shows the split, the e-mail-ready share and reloads for another Top N', async () => {
    api.channelStatistics.mockResolvedValue({
      top: 100, vacancies: 4, emailReady: 1, emailReadyPercent: 25,
      channels: [{ channel: 'Email', count: 1, percent: 25 }, { channel: 'Portal', count: 3, percent: 75 }],
      topEmailReady: [{ vacancyId: 'v1', title: 'Entwickler', company: 'Firma', rankScore: 90, email: 'jobs@firma.de', contactName: 'Lisa', source: 'Rule' }],
      comparison: { withAi: 4, sameChannel: 3, bothSameEmail: 1, rulesOnlyEmail: 0, aiOnlyEmail: 0, differentEmail: 0, aiEmailRejected: 1 },
    })
    const wrapper = await mountSuspended(ChannelPanel)
    await flushPromises()

    expect(api.channelStatistics).toHaveBeenLastCalledWith(100)
    expect(wrapper.text()).toContain('1 von 4 (25 %) vollständig vorbereitbar')
    expect(wrapper.text()).toContain('jobs@firma.de')
    expect(wrapper.text()).toContain('KI-Adresse verworfen (nicht im Text) 1')

    await wrapper.find('select').setValue('50')
    await flushPromises()
    expect(api.channelStatistics).toHaveBeenLastCalledWith(50)
  })
})
