import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import ManualVacancyPage from '~/pages/vacancies/neu.vue'

const api = vi.hoisted(() => ({ createVacancy: vi.fn(), labelSummary: vi.fn() }))
mockNuxtImport('useVacanciesApi', () => () => api)
const navigate = vi.hoisted(() => vi.fn())
mockNuxtImport('navigateTo', () => navigate)

describe('Stelle manuell hinzufügen', () => {
  it('sends the pasted posting and opens the new vacancy', async () => {
    api.createVacancy.mockResolvedValue({ id: 'v9' })
    const wrapper = await mountSuspended(ManualVacancyPage)

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.find('#manual-title').setValue('Senior Full Stack Engineer')
    await wrapper.find('#manual-company').setValue('Boardwise')
    await wrapper.find('#manual-url').setValue('https://www.linkedin.com/jobs/view/1')
    await wrapper.find('#manual-description').setValue('Wir entwickeln mit C# und Vue.js.')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.createVacancy).toHaveBeenCalledWith({
      title: 'Senior Full Stack Engineer', company: 'Boardwise', location: null,
      url: 'https://www.linkedin.com/jobs/view/1', description: 'Wir entwickeln mit C# und Vue.js.',
    })
    expect(navigate).toHaveBeenCalledWith('/vacancies/v9')
  })
})
