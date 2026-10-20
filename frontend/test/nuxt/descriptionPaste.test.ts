import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import DescriptionPaste from '~/components/DescriptionPaste.vue'

const api = vi.hoisted(() => ({ setDescription: vi.fn() }))
mockNuxtImport('useVacanciesApi', () => () => api)

describe('DescriptionPaste', () => {
  it('sends the pasted text and hands the re-scored vacancy back', async () => {
    const scored = { id: 'v1', description: 'Wir entwickeln mit C#.', descriptionIsManual: true }
    api.setDescription.mockResolvedValue(scored)
    const wrapper = await mountSuspended(DescriptionPaste, { props: { vacancyId: 'v1' } })

    await wrapper.find('button').trigger('click')
    await wrapper.find('textarea').setValue('Wir entwickeln mit C#.')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.setDescription).toHaveBeenCalledWith('v1', 'Wir entwickeln mit C#.')
    expect(wrapper.emitted('saved')![0]).toEqual([scored])
    expect(wrapper.find('textarea').exists()).toBe(false)
  })

  it('does not send an empty text', async () => {
    api.setDescription.mockClear()
    const wrapper = await mountSuspended(DescriptionPaste, { props: { vacancyId: 'v1' } })

    await wrapper.find('button').trigger('click')
    await wrapper.find('form').trigger('submit')

    expect(api.setDescription).not.toHaveBeenCalled()
    expect(wrapper.find('[role="alert"]').text()).toContain('Bitte den Text')
  })
})
