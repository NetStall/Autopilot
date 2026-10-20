import type {
  EmployerTypePolicy,
  RemotePreference,
  SearchProfile,
  SearchTermScope,
  StackKeyword,
} from '../types/searchProfile'

export const SCOPE_OPTIONS: readonly { value: SearchTermScope, label: string }[] = [
  { value: 'Both', label: 'Region + Remote' },
  { value: 'Region', label: 'Nur Region' },
  { value: 'Remote', label: 'Nur Remote' },
]

export const REMOTE_OPTIONS: readonly { value: RemotePreference, label: string, hint: string }[] = [
  { value: 'Any', label: 'Egal', hint: 'Kein Abzug' },
  { value: 'AtLeastHybrid', label: 'Mindestens Hybrid', hint: 'Abzug für Stellen nur vor Ort' },
  { value: 'FullRemoteOnly', label: 'Nur voll remote', hint: 'Abzug für alles außer voll remote' },
]

export const POLICY_OPTIONS: readonly { value: EmployerTypePolicy, label: string }[] = [
  { value: 'Allow', label: 'Zulassen' },
  { value: 'RankLower', label: 'Niedriger einordnen' },
  { value: 'Hide', label: 'Ausblenden' },
]

/** Default weight of a new keyword per list. */
export const DEFAULT_WEIGHTS = { mustHave: 20, niceToHave: 5, exclude: 30 } as const

/** Upper weight limits per list (the server validates the same). */
export const MAX_WEIGHTS = { mustHave: 50, niceToHave: 50, exclude: 100 } as const

/** Bounds of the „Fit-Abzüge" inputs. */
export const MAX_PENALTY = 50

export function cloneProfile(profile: SearchProfile): SearchProfile {
  return JSON.parse(JSON.stringify(profile)) as SearchProfile
}

/** Unsaved changes compared with the loaded version. */
export function isDirty(current: SearchProfile, saved: SearchProfile | null | undefined): boolean {
  return saved != null && JSON.stringify(current) !== JSON.stringify(saved)
}

/** „C#, .NET; Vue" or one per line → trimmed, without empties and case-insensitive duplicates (also of `existing`). */
export function parseTags(input: string, existing: readonly string[] = []): string[] {
  const seen = new Set(existing.map(e => e.toLocaleLowerCase('de')))
  const result: string[] = []
  for (const part of input.split(/[,;\n]/)) {
    const tag = part.trim()
    const key = tag.toLocaleLowerCase('de')
    if (tag && !seen.has(key)) {
      seen.add(key)
      result.push(tag)
    }
  }
  return result
}

export function newKeyword(name: string, weight: number): StackKeyword {
  return {
    name,
    aliases: [],
    weight,
    group: null,
    core: false,
    titleOnly: false,
    matchCase: false,
    softenedByCore: false,
    reason: null,
  }
}

/**
 * Reads an uploaded export. Returns the profile or a German error; the server validates the content afterwards
 * (preview) before it is loaded into the form.
 */
export function parseImportedProfile(text: string): { profile: SearchProfile } | { error: string } {
  let value: unknown
  try {
    value = JSON.parse(text)
  }
  catch {
    return { error: 'Die Datei ist kein gültiges JSON.' }
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return { error: 'Die Datei enthält kein Suchprofil.' }
  }
  const candidate = value as Partial<SearchProfile>
  if (!Array.isArray(candidate.searchTerms) || !Array.isArray(candidate.places) || typeof candidate.penalties !== 'object') {
    return { error: 'Die Datei enthält kein Suchprofil (searchTerms, places oder penalties fehlen).' }
  }
  return { profile: candidate as SearchProfile }
}

/** „Mit diesen Einstellungen: 142 passende Stellen (davon 31 voll remote)". */
export function previewLine(matching: number, fullRemote: number, currentMatching: number): string {
  const diff = matching - currentMatching
  const change = diff === 0 ? 'wie bisher' : diff > 0 ? `${diff} mehr als bisher` : `${-diff} weniger als bisher`
  return `Mit diesen Einstellungen: ${matching} passende Stellen (davon ${fullRemote} voll remote) – ${change}`
}
