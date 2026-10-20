import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import type { ApplicationDocuments } from '~/types/documents'
import ReviewPage from '~/pages/unterlagen/[id].vue'
import { documents } from './fixtures'

const api = vi.hoisted(() => ({
  documents: vi.fn(),
  saveDocuments: vi.fn(),
  approveDocuments: vi.fn(),
  markSent: vi.fn(),
  rephraseParagraph: vi.fn(),
  regenerateLetter: vi.fn(),
  restoreDocuments: vi.fn(),
  createDocuments: vi.fn(),
  previewDocument: vi.fn(),
  documentPdfUrl: vi.fn((id: string, kind: string) => `http://api.test/api/applications/${id}/documents/${kind}.pdf`),
}))

mockNuxtImport('useVacanciesApi', () => () => api)

async function mountPage(data: ApplicationDocuments) {
  api.documents.mockResolvedValue(data)
  const wrapper = await mountSuspended(ReviewPage, { route: `/unterlagen/${data.applicationId}` })
  await flushPromises()
  return wrapper
}

function button(wrapper: Awaited<ReturnType<typeof mountPage>>, text: string) {
  const found = wrapper.findAll('button').find(b => b.text() === text)
  if (!found) {
    throw new Error(`No button „${text}“`)
  }
  return found
}

describe('review screen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.previewDocument.mockResolvedValue({ url: 'blob:preview', pages: 1 })
  })

  it('loads the documents and shows letter, warnings, cheat sheet and usage', async () => {
    const wrapper = await mountPage(documents())

    expect(api.documents).toHaveBeenCalledWith('app-1')
    expect(wrapper.find('h1').text()).toBe('Full-Stack-Entwickler (m/w/d)')
    expect(wrapper.findAll('textarea')).toHaveLength(3)
    expect(wrapper.text()).toContain('Faktenprüfung')
    expect(wrapper.text()).toContain('Spickzettel')
    expect(wrapper.find('.docs__usage').text()).toContain('3.900 Tokens')
  })

  it('approval with open warnings needs the explicit confirmation', async () => {
    api.approveDocuments.mockResolvedValue(documents({ status: 'Approved', statusLabel: 'Freigegeben', approvedAt: '2026-10-20T09:00:00Z', warningsAccepted: true, letterFile: 'A.pdf', cvFile: 'B.pdf' }))
    const wrapper = await mountPage(documents())

    const approve = button(wrapper, 'Freigeben')
    expect(approve.attributes('disabled')).toBeDefined()

    await wrapper.find('.docs__check input').setValue(true)
    expect(approve.attributes('disabled')).toBeUndefined()
    await approve.trigger('click')
    await flushPromises()

    expect(api.approveDocuments).toHaveBeenCalledWith('app-1', 3, true)
    expect(wrapper.text()).toContain('mit bestätigten Warnungen')
    expect(wrapper.find('a[download]').attributes('href')).toBe('http://api.test/api/applications/app-1/documents/letter.pdf')
  })

  it('without warnings approval needs no checkbox', async () => {
    const wrapper = await mountPage(documents({ warnings: [], hasOpenWarnings: false }))

    expect(wrapper.find('.docs__check').exists()).toBe(false)
    expect(button(wrapper, 'Freigeben').attributes('disabled')).toBeUndefined()
  })

  it('unsaved edits must be saved before approving', async () => {
    api.saveDocuments.mockImplementation((_id: string, _version: number, letter: ApplicationDocuments['letter']) =>
      Promise.resolve(documents({ version: 4, letter, warnings: [], hasOpenWarnings: false })))
    const wrapper = await mountPage(documents({ warnings: [], hasOpenWarnings: false }))

    await wrapper.findAll('textarea')[1]!.setValue('Bei der Muster Software GmbH habe ich REST-APIs entwickelt.')

    expect(wrapper.text()).toContain('Ungespeicherte Änderungen')
    expect(button(wrapper, 'Freigeben').attributes('disabled')).toBeDefined()
    await button(wrapper, 'Speichern').trigger('click')
    await flushPromises()

    expect(api.saveDocuments).toHaveBeenCalledWith('app-1', 3, expect.objectContaining({ paragraphs: expect.arrayContaining(['Bei der Muster Software GmbH habe ich REST-APIs entwickelt.']) }), expect.anything())
    expect(wrapper.text()).not.toContain('Ungespeicherte Änderungen')
    expect(button(wrapper, 'Freigeben').attributes('disabled')).toBeUndefined()
  })

  it('marking as sent is only offered after approval', async () => {
    const draft = await mountPage(documents())
    expect(draft.text()).not.toContain('Als gesendet markieren')

    vi.stubGlobal('confirm', () => true)
    api.markSent.mockResolvedValue(documents({ status: 'Sent', statusLabel: 'Gesendet', approvedAt: '2026-10-20T09:00:00Z' }))
    const approved = await mountPage(documents({ status: 'Approved', statusLabel: 'Freigegeben', approvedAt: '2026-10-20T09:00:00Z', letterFile: 'A.pdf', cvFile: 'B.pdf' }))

    await approved.find('#send-channel').setValue('Bewerbungsportal')
    await approved.find('form.docs__send').trigger('submit')
    await flushPromises()

    expect(api.markSent).toHaveBeenCalledWith('app-1', 'Bewerbungsportal', expect.any(String))
    expect(approved.find('textarea').attributes('disabled')).toBeDefined()
    vi.unstubAllGlobals()
  })

  it('a version conflict offers to reload', async () => {
    api.saveDocuments.mockRejectedValue({ statusCode: 409, data: { title: 'Konflikt.' } })
    const wrapper = await mountPage(documents({ warnings: [], hasOpenWarnings: false }))

    await wrapper.findAll('textarea')[0]!.setValue('Geändert.')
    await button(wrapper, 'Speichern').trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('an anderer Stelle geändert')
  })
})
