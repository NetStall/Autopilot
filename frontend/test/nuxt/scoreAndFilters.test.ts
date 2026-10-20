import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import type { VacancyFacets } from '~/types/vacancy'
import type { VacancyFilters as Filters } from '~/utils/vacancyFilters'
import ScoreBadge from '~/components/ScoreBadge.vue'
import VacancyReasons from '~/components/VacancyReasons.vue'
import VacancyFilters from '~/components/VacancyFilters.vue'

const api = vi.hoisted(() => ({
  uiSettings: vi.fn().mockResolvedValue({ highlightKeywords: [], scoreHigh: 70, scoreMedium: 45 }),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

const facets: VacancyFacets = {
  source: { Arbeitsagentur: 268, Himalayas: 51 },
  remoteLevel: { FullRemote: 30, Hybrid: 100, OnSite: 50, Unknown: 139 },
  employerType: { Direct: 59, Agency: 7, Temporary: 2, Unknown: 251 },
  relevanceLevel: { Relevant: 102, Maybe: 217, Irrelevant: 941 },
  requiredGerman: { C1Plus: 40, B2: 120, B1OrLower: 15, None: 30, Unknown: 114 },
}

describe('ScoreBadge', () => {
  it('always shows the number, with an aria-label and the colour as an extra', async () => {
    const wrapper = await mountSuspended(ScoreBadge, { props: { vacancy: { relevanceScore: 94, rankScore: 86, relevanceLevel: 'Relevant' } } })

    const score = wrapper.find('.score')
    expect(score.text()).toBe('86')
    expect(score.attributes('role')).toBe('img')
    expect(score.attributes('aria-label')).toBe('Bewertung 86 von 100, Relevanz 94 · Abzüge −8')
    expect(score.attributes('title')).toBe('Relevanz 94 · Abzüge −8')
    expect(score.classes()).toContain('score--high')
  })

  it.each([
    [50, 'score--medium'],
    [30, 'score--low'],
  ])('colours %i as %s', async (rank, tone) => {
    const wrapper = await mountSuspended(ScoreBadge, { props: { vacancy: { relevanceScore: rank, rankScore: rank, relevanceLevel: 'Maybe' } } })

    expect(wrapper.find('.score').classes()).toContain(tone)
    expect(wrapper.find('.score').text()).toBe(String(rank))
  })

  it('shows a dash before scoring and the detail line on demand', async () => {
    const unscored = await mountSuspended(ScoreBadge, { props: { vacancy: { relevanceScore: null, rankScore: null, relevanceLevel: null } } })
    expect(unscored.find('.score').text()).toBe('–')
    expect(unscored.find('.score').attributes('aria-label')).toBe('Noch nicht bewertet')
    expect(unscored.find('.score').classes()).toContain('score--unscored')

    const detail = await mountSuspended(ScoreBadge, { props: { vacancy: { relevanceScore: 80, rankScore: 80, relevanceLevel: 'Relevant' }, showDetail: true } })
    expect(detail.find('.score-badge__detail').text()).toBe('Relevanz 80 · keine Abzüge')
  })
})

describe('„Warum?" reasons', () => {
  it('renders each reason as text with its kind', async () => {
    const wrapper = await mountSuspended(VacancyReasons, {
      props: {
        reasons: ['C# im Titel ✓ (+50)', 'Java als Hauptsprache im Titel ✗ (-60)', 'Java neben C#/.NET im Titel – abgeschwächt (-15)', 'Voll remote: „100 % remote“', '<b>kein HTML</b>'],
      },
    })

    const items = wrapper.findAll('li')
    expect(items.map(i => i.classes().find(c => c.startsWith('reasons__item--')))).toEqual([
      'reasons__item--positive', 'reasons__item--negative', 'reasons__item--softened', 'reasons__item--info', 'reasons__item--info',
    ])
    expect(items[4]!.text()).toBe('<b>kein HTML</b>')
    expect(wrapper.find('b').exists()).toBe(false)
  })

  it('says when nothing was scored yet', async () => {
    const wrapper = await mountSuspended(VacancyReasons, { props: { reasons: [] } })

    expect(wrapper.text()).toContain('Noch nicht bewertet')
  })
})

describe('VacancyFilters', () => {
  async function mountFilters(filters: Partial<Filters> = {}, props: Record<string, unknown> = {}) {
    return mountSuspended(VacancyFilters, {
      props: { filters: { ...defaultFilters(), ...filters }, facets, defaultMinScore: 40, topN: 20, ...props },
    })
  }

  function lastChange(wrapper: Awaited<ReturnType<typeof mountFilters>>): Filters {
    return wrapper.emitted('change')!.at(-1)![0] as Filters
  }

  it('shows the Suchprofil default and writes other values in steps of 5', async () => {
    const wrapper = await mountFilters()

    const number = wrapper.find('input[type="number"]')
    expect((wrapper.find('input[type="range"]').element as HTMLInputElement).value).toBe('40')
    expect((number.element as HTMLInputElement).value).toBe('40')

    await number.setValue('62')
    await number.trigger('change')
    expect(lastChange(wrapper).minScore).toBe(60)

    await number.setValue('40')
    await number.trigger('change')
    expect(lastChange(wrapper).minScore).toBeNull()
  })

  it('„Nur Top N" sets top to the Suchprofil N', async () => {
    const wrapper = await mountFilters()

    const top = wrapper.findAll('input[type="checkbox"]').find(i => i.element.parentElement?.textContent?.includes('Nur Top 20'))!
    await top.setValue(true)

    expect(lastChange(wrapper).top).toBe(20)
  })

  it('lists sources, relevance, employer and German levels with their counts', async () => {
    const wrapper = await mountFilters()
    const text = wrapper.text()

    expect(text).toContain('Arbeitsagentur (268)')
    expect(text).toContain('Himalayas (51)')
    expect(text).toContain('Manuell erfasst (0)')
    expect(text).toContain('Passt (102)')
    expect(text).toContain('Personalvermittlung (7)')
    expect(text).toContain('B2 (120)')
    expect(text).toContain('Kein Deutsch nötig (30)')
  })

  it('toggles a source and a German level', async () => {
    const wrapper = await mountFilters()

    await wrapper.find('input[name="source"][value="Himalayas"]').setValue(true)
    expect(lastChange(wrapper).sources).toEqual(['Himalayas'])

    await wrapper.find('input[name="german"][value="C1Plus"]').setValue(false)
    expect(lastChange(wrapper).german).toEqual(['B2', 'B1OrLower', 'None', 'Unknown'])
  })

  it('a German default from the Suchprofil is preselected and not written back', async () => {
    const wrapper = await mountFilters({}, { defaultGerman: ['B2', 'B1OrLower'] })

    expect((wrapper.find('input[name="german"][value="B2"]').element as HTMLInputElement).checked).toBe(true)
    expect((wrapper.find('input[name="german"][value="C1Plus"]').element as HTMLInputElement).checked).toBe(false)

    await wrapper.find('input[name="german"][value="None"]').setValue(true)
    expect(lastChange(wrapper).german).toEqual(['B2', 'B1OrLower', 'None'])
    await wrapper.setProps({ filters: { ...defaultFilters(), german: ['B2', 'B1OrLower', 'None'] } })
    await wrapper.find('input[name="german"][value="None"]').setValue(false)
    expect(lastChange(wrapper).german).toBeNull()
  })
})
