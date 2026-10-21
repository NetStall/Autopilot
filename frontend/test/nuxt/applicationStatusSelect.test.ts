import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import ApplicationStatusSelect from '~/components/ApplicationStatusSelect.vue'

const api = vi.hoisted(() => ({ changeApplicationStatus: vi.fn() }))
mockNuxtImport('useVacanciesApi', () => () => api)

describe('ApplicationStatusSelect (08a)', () => {
  it('sets „Absage" from the vacancy page and reports the new application', async () => {
    api.changeApplicationStatus.mockResolvedValue({ id: 'a1', status: 'Rejected', statusLabel: 'Absage', appliedAt: '2026-10-01T08:00:00Z' })
    const wrapper = await mountSuspended(ApplicationStatusSelect, { props: { applicationId: 'a1', status: 'Sent' } })

    const labels = wrapper.findAll('option').map(o => o.text())
    expect(labels).toContain('Absage')
    expect(labels).not.toContain('Entwurf')
    await wrapper.find('select').setValue('Rejected')
    await flushPromises()

    expect(api.changeApplicationStatus).toHaveBeenCalledWith('a1', 'Rejected')
    expect(wrapper.emitted('changed')![0]![0]).toMatchObject({ status: 'Rejected' })
  })

  it('offers only „Zurückgezogen" for a draft', async () => {
    const wrapper = await mountSuspended(ApplicationStatusSelect, { props: { applicationId: 'a1', status: 'Draft' } })

    expect(wrapper.findAll('option').map(o => o.text())).toEqual(['Entwurf', 'Zurückgezogen'])
  })
})
