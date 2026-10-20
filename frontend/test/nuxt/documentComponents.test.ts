import { afterEach, describe, expect, it, vi } from 'vitest'
import { mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import type { CvOptions, Letter } from '~/types/documents'
import LetterEditor from '~/components/LetterEditor.vue'
import DraftWarnings from '~/components/DraftWarnings.vue'
import ApplicationCheatSheet from '~/components/ApplicationCheatSheet.vue'
import CvOptionsEditor from '~/components/CvOptionsEditor.vue'
import { documents, letter } from './fixtures'

describe('LetterEditor', () => {
  it('shows each paragraph with its label, length and warnings', async () => {
    const wrapper = await mountSuspended(LetterEditor, { props: { modelValue: letter, warnings: documents().warnings } })

    const areas = wrapper.findAll('textarea')
    expect(areas).toHaveLength(3)
    expect(wrapper.find('label[for="letter-paragraph-1"]').text()).toBe('Absatz 2')
    expect(areas[1]!.classes()).toContain('letter__text--warning')
    expect(areas[1]!.attributes('aria-describedby')).toBe('letter-paragraph-1-warnings')
    expect(wrapper.find('#letter-paragraph-1-warnings').text()).toContain('Warnung: Die Angabe „12“')
    expect(wrapper.find('#letter-paragraph-0-warnings').text()).toContain('Hinweis:')
    expect(wrapper.find('.letter__length').text()).toMatch(/Zeichen · voraussichtlich 1 Seite/)
  })

  it('edits a paragraph through v-model and asks for a rephrasing', async () => {
    const wrapper = await mountSuspended(LetterEditor, { props: { modelValue: letter } })

    await wrapper.findAll('textarea')[0]!.setValue('Neuer Text.')
    const updated = wrapper.emitted('update:modelValue')!.at(-1)![0] as Letter
    expect(updated.paragraphs).toEqual(['Neuer Text.', letter.paragraphs[1], letter.paragraphs[2]])

    const buttons = wrapper.findAll('button').filter(b => b.text() === 'Absatz neu formulieren')
    await buttons[2]!.trigger('click')
    expect(wrapper.emitted('rephrase')).toEqual([[2]])
  })

  it('blocks rephrasing while one paragraph is being rewritten', async () => {
    const wrapper = await mountSuspended(LetterEditor, { props: { modelValue: letter, rephrasing: 1 } })

    const buttons = wrapper.findAll('.letter__paragraph-head button')
    expect(buttons.every(b => b.attributes('disabled') !== undefined)).toBe(true)
    expect(buttons[1]!.text()).toBe('Wird neu formuliert …')
  })
})

describe('DraftWarnings', () => {
  it('separates warnings from hints and links to the paragraph', async () => {
    const wrapper = await mountSuspended(DraftWarnings, { props: { warnings: documents().warnings } })

    const warnings = wrapper.find('ul[aria-label="Warnungen"]')
    expect(warnings.text()).toContain('Warnung:')
    expect(warnings.find('a').attributes('href')).toBe('#letter-paragraph-1')
    expect(wrapper.find('ul[aria-label="Hinweise"]').text()).toContain('Floskel')
  })

  it('says when nothing was found', async () => {
    const wrapper = await mountSuspended(DraftWarnings, { props: { warnings: [] } })

    expect(wrapper.text()).toContain('Keine Auffälligkeiten')
  })
})

describe('ApplicationCheatSheet', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('copies a value and confirms it', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
    const wrapper = await mountSuspended(ApplicationCheatSheet, { props: { items: documents().cheatSheet } })

    const button = wrapper.find('button[aria-label="Gehaltsvorstellung kopieren"]')
    await button.trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith('50.000–60.000 € brutto/Jahr')
    expect(button.text()).toBe('Kopiert ✓')
    expect(wrapper.find('[aria-live="polite"]').text()).toBe('In die Zwischenablage kopiert.')
    expect(wrapper.find('pre').text()).toContain('anbei')
  })

  it('explains when the clipboard is not available', async () => {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } })
    const wrapper = await mountSuspended(ApplicationCheatSheet, { props: { items: documents().cheatSheet } })

    await wrapper.find('button[aria-label="Betreff kopieren"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('Kopieren nicht möglich')
  })
})

describe('CvOptionsEditor', () => {
  it('hides a bullet and moves a skill group', async () => {
    const docs = documents()
    const wrapper = await mountSuspended(CvOptionsEditor, { props: { cv: docs.cv, modelValue: docs.cvOptions } })

    await wrapper.findAll('input[type="checkbox"]')[1]!.setValue(false)
    expect((wrapper.emitted('update:modelValue')!.at(-1)![0] as CvOptions).hiddenBullets).toEqual(['exp0.b1'])

    await wrapper.find('button[aria-label="Nach oben: Frontend"]').trigger('click')
    expect((wrapper.emitted('update:modelValue')!.at(-1)![0] as CvOptions).skillGroupOrder).toEqual(['Frontend', 'Backend'])
    expect(wrapper.find('button[aria-label="Nach oben: Backend"]').attributes('disabled')).toBeDefined()
  })
})
