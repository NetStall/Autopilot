import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import HeuteBereit from '~/pages/heute-bereit.vue'
import type { ApplicationPackage } from '~/types/package'

const pkg: ApplicationPackage = {
  applicationId: 'a1', vacancyId: 'v1', company: 'Beispiel GmbH', position: 'Entwickler (m/w/d)', status: 'Approved', statusLabel: 'Freigegeben',
  approved: true,
  howToApply: { channel: 'Email', email: 'jobs@beispiel.de', contactName: 'Frau Anna Müller', salutation: 'Sehr geehrte Frau Müller,', emailAllowed: true, portalOnly: false, requested: [], source: 'Rule', externalUrl: null },
  email: { recipient: 'jobs@beispiel.de', subject: 'Bewerbung als Entwickler (m/w/d)', salutation: 'Sehr geehrte Frau Müller,', body: 'anbei meine Bewerbung.', closing: 'Mit freundlichen Grüßen', senderName: 'Alex Beispiel', attachments: ['Alex_Beispiel_Anschreiben_Beispiel_GmbH.pdf', 'Alex_Beispiel_Lebenslauf_Beispiel_GmbH.pdf'] },
  portal: { checklist: [], motivation: '', aboutMe: '', postingUrl: null },
  warnings: [], letterWarnings: [], generatedAt: '2026-10-22T08:00:00Z', version: 3,
}

const api = vi.hoisted(() => ({
  readyPackages: vi.fn(),
  followUps: vi.fn(),
  packageEstimate: vi.fn(),
  getPackage: vi.fn(),
  prepareTopPackages: vi.fn(),
  markSent: vi.fn(),
  changeApplicationStatus: vi.fn(),
  followUpDone: vi.fn(),
  packageZipUrl: (id: string) => `http://api/api/applications/${id}/package.zip`,
}))
mockNuxtImport('useVacanciesApi', () => () => api)

describe('Heute bereit (08c)', () => {
  it('lists packages and due follow-ups, opens the e-mail part and marks as sent only after approval', async () => {
    api.readyPackages.mockResolvedValue([
      { applicationId: 'a1', vacancyId: 'v1', company: 'Beispiel GmbH', position: 'Entwickler (m/w/d)', status: 'Approved', channel: 'Email', email: 'jobs@beispiel.de', approved: true, warnings: 0, generatedAt: '2026-10-22T08:00:00Z' },
      { applicationId: 'a2', vacancyId: 'v2', company: 'Portal AG', position: 'Backend', status: 'Draft', channel: 'Portal', email: null, approved: false, warnings: 1, generatedAt: '2026-10-22T09:00:00Z' },
    ])
    api.followUps.mockResolvedValue([{ applicationId: 'a9', company: 'Alt GmbH', position: 'Dev', status: 'Sent', statusLabel: 'Gesendet', appliedAt: '2026-10-01T08:00:00Z', lastActionAt: '2026-10-01T08:00:00Z', dueAt: '2026-10-08T08:00:00Z', draft: { subject: 'Nachfrage zu meiner Bewerbung als Dev', email: 'Sehr geehrte Damen und Herren, …', linkedIn: 'Guten Tag, …' } }])
    api.packageEstimate.mockResolvedValue({ k: 5, candidates: [{ vacancyId: 'v3', title: 'Neu', company: 'Firma', rankScore: 80 }], calls: 2, costUsd: 0.04, remainingCalls: 28, withinLimit: true })
    api.getPackage.mockResolvedValue(pkg)
    api.markSent.mockResolvedValue({})

    const wrapper = await mountSuspended(HeuteBereit)
    await flushPromises()

    expect(wrapper.text()).toContain('Pakete (2)')
    expect(wrapper.text()).toContain('≈ 0.04 $')
    expect(wrapper.text()).toContain('Nachfrage zu meiner Bewerbung als Dev')
    const sentButtons = wrapper.findAll('button').filter(b => b.text() === 'Als gesendet markieren')
    expect(sentButtons[1]!.attributes('disabled')).toBeDefined()

    await wrapper.findAll('button').find(b => b.text() === 'Öffnen')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('jobs@beispiel.de')
    expect(wrapper.find('a[href^="mailto:jobs@beispiel.de"]').exists()).toBe(true)
    expect(wrapper.find('a[href$="package.zip"]').exists()).toBe(true)

    await sentButtons[0]!.trigger('click')
    await flushPromises()
    expect(api.markSent).toHaveBeenCalledWith('a1', 'E-Mail')
  })
})
