import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import DailyRunPanel from '~/components/DailyRunPanel.vue'

const idle = {
  state: 'Idle', currentStage: null,
  stages: [{ stage: 'Extraction', outcome: 'Skipped', message: 'Lokales Modell nicht erreichbar – KI-Analyse übersprungen.', durationMs: 5 }],
  last: {
    startedAt: '2026-10-22T08:00:00Z', finishedAt: '2026-10-22T08:05:00Z', newVacancies: 12, newFiltered: { Stack: 3 },
    newInTop20: [{ vacancyId: 'v1', title: 'Entwickler C#', company: 'Firma', rankScore: 88 }], emailReadyInTop20: [], failures: [], stages: [],
  },
}

const api = vi.hoisted(() => ({
  dailyRun: vi.fn(),
  startDailyRun: vi.fn(),
  cancelDailyRun: vi.fn(),
  coverage: vi.fn().mockResolvedValue({ days: 7, vacancies: 3, searches: [{ name: 'region-csharp', source: 'Arbeitsagentur', configured: true, vacancies: 3, inTop50: 2 }], bringNothing: ['remote-go'] }),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

describe('DailyRunPanel (08b)', () => {
  it('shows the last summary, a skipped KI stage and the coverage, and starts a run', async () => {
    api.dailyRun.mockResolvedValue(idle)
    api.startDailyRun.mockResolvedValue({ ...idle, state: 'Queued', stages: [] })
    const wrapper = await mountSuspended(DailyRunPanel)
    await flushPromises()

    expect(wrapper.text()).toContain('12 neue Stellen')
    expect(wrapper.text()).toContain('Stack 3')
    expect(wrapper.text()).toContain('KI-Analyse übersprungen')
    expect(wrapper.text()).toContain('Bringen nichts in die Top 50: remote-go')

    await wrapper.find('button').trigger('click')
    expect(api.startDailyRun).toHaveBeenCalledWith(100)
    wrapper.unmount()
  })
})
