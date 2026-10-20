// Mirrors JobAutopilot.Core.SearchProfiles (GET/PUT /api/search-profile).

import type { ConfiguredSearch, RequiredGerman } from './vacancy'

export type SearchTermScope = 'Both' | 'Region' | 'Remote'
export type RemotePreference = 'Any' | 'AtLeastHybrid' | 'FullRemoteOnly'
export type EmployerTypePolicy = 'Allow' | 'RankLower' | 'Hide'

export interface SearchTerm {
  text: string
  scope: SearchTermScope
}

export interface SearchPlace {
  city: string
  radiusKm: number
}

export interface StackKeyword {
  name: string
  aliases: string[]
  weight: number
  group: string | null
  core: boolean
  titleOnly: boolean
  matchCase: boolean
  softenedByCore: boolean
  reason: string | null
}

export interface SearchPenalties {
  germanPerLevel: number
  experience: number
  experienceStrong: number
  mustHaveGap: number
  requiredGap: number
  niceToHaveGap: number
  requiredGapMax: number
  outsideRegion: number
  employerType: number
  remotePreference: number
}

export interface SearchProfile {
  searchTerms: SearchTerm[]
  places: SearchPlace[]
  additionalPlaces: string[]
  remoteGermany: boolean
  remotePreference: RemotePreference
  mustHave: StackKeyword[]
  niceToHave: StackKeyword[]
  exclude: StackKeyword[]
  titleExclusions: string[]
  agency: EmployerTypePolicy
  temporary: EmployerTypePolicy
  publishedWithinDays: number
  sources: { arbeitsagentur: boolean, himalayas: boolean }
  minScore: number
  topN: number
  /** Default of „Deutsch erforderlich" while the URL has none; empty = all. */
  germanFilter: RequiredGerman[]
  penalties: SearchPenalties
  salaryMin: number | null
  salaryMax: number | null
  earliestStart: string | null
  maxCommuteMinutes: number | null
}

export interface SearchProfileResponse {
  version: number
  updatedAt: string | null
  profile: SearchProfile
  searches: ConfiguredSearch[]
}

export interface SearchProfilePreview {
  errors: string[]
  matching: number
  fullRemote: number
  currentMatching: number
  scored: number
  searches: ConfiguredSearch[]
}

export interface SearchProfileRevision {
  version: number
  savedAt: string
  summary: string
}

export interface SaveSearchProfileResponse {
  searchProfile: SearchProfileResponse
  rescore: { state: 'Idle' | 'Queued' | 'Running' }
}
