import { describe, expect, it } from 'vitest'
import type { SearchProfile } from '../app/types/searchProfile'
import {
  cloneProfile,
  isDirty,
  newKeyword,
  parseImportedProfile,
  parseTags,
  previewLine,
} from '../app/utils/searchProfileForm'

const profile: SearchProfile = {
  searchTerms: [{ text: '.NET Entwickler', scope: 'Both' }],
  places: [{ city: 'Talstadt', radiusKm: 50 }],
  additionalPlaces: ['Plattling'],
  remoteGermany: true,
  remotePreference: 'AtLeastHybrid',
  mustHave: [newKeyword('C#', 20)],
  niceToHave: [],
  exclude: [newKeyword('Java', 30)],
  titleExclusions: ['Praktikum'],
  agency: 'RankLower',
  temporary: 'RankLower',
  publishedWithinDays: 30,
  sources: { arbeitsagentur: true, himalayas: true },
  minScore: 0,
  topN: 20,
  penalties: {
    germanPerLevel: 8,
    experience: 8,
    requiredGap: 6,
    requiredGapMax: 18,
    outsideRegion: 15,
    employerType: 10,
    remotePreference: 10,
  },
  salaryMin: null,
  salaryMax: null,
  earliestStart: null,
  maxCommuteMinutes: null,
}

describe('parseTags', () => {
  it('splits on comma, semicolon and new lines and trims', () => {
    expect(parseTags(' C#, .NET; Vue\nNuxt ,, ')).toEqual(['C#', '.NET', 'Vue', 'Nuxt'])
  })

  it('drops case-insensitive duplicates, also of existing tags', () => {
    expect(parseTags('vue, Vue, Brunnfeld', ['VUE'])).toEqual(['Brunnfeld'])
    expect(parseTags('Ärzte, ärzte')).toEqual(['Ärzte'])
  })

  it('returns nothing for blank input', () => {
    expect(parseTags('   ')).toEqual([])
  })
})

describe('isDirty', () => {
  it('is false for the loaded profile and its clone', () => {
    expect(isDirty(cloneProfile(profile), profile)).toBe(false)
  })

  it('notices nested changes (unsaved-changes warning)', () => {
    const changed = cloneProfile(profile)
    changed.places[0]!.radiusKm = 75
    expect(isDirty(changed, profile)).toBe(true)

    const tagged = cloneProfile(profile)
    tagged.mustHave[0]!.aliases.push('csharp')
    expect(isDirty(tagged, profile)).toBe(true)
  })

  it('is false while nothing is loaded', () => {
    expect(isDirty(profile, null)).toBe(false)
  })

  it('does not share state with the clone', () => {
    const clone = cloneProfile(profile)
    clone.titleExclusions.push('Werkstudent')
    expect(profile.titleExclusions).toEqual(['Praktikum'])
  })
})

describe('parseImportedProfile', () => {
  it('accepts an exported Suchprofil', () => {
    expect(parseImportedProfile(JSON.stringify(profile))).toEqual({ profile })
  })

  it.each([
    ['not json', 'Die Datei ist kein gültiges JSON.'],
    ['[1, 2]', 'Die Datei enthält kein Suchprofil.'],
    ['null', 'Die Datei enthält kein Suchprofil.'],
    ['{"name": "Vitalii"}', 'Die Datei enthält kein Suchprofil (searchTerms, places oder penalties fehlen).'],
  ])('rejects %s', (text, error) => {
    expect(parseImportedProfile(text)).toEqual({ error })
  })
})

describe('newKeyword', () => {
  it('starts without aliases and options', () => {
    expect(newKeyword('Vue', 5)).toMatchObject({ name: 'Vue', weight: 5, aliases: [], group: null, core: false })
  })
})

describe('previewLine', () => {
  it('describes the count and the change', () => {
    expect(previewLine(142, 31, 120)).toBe('Mit diesen Einstellungen: 142 passende Stellen (davon 31 voll remote) – 22 mehr als bisher')
    expect(previewLine(100, 0, 120)).toContain('20 weniger als bisher')
    expect(previewLine(120, 5, 120)).toContain('wie bisher')
  })
})
