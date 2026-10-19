// Mirrors JobAutopilot.Core.Profile (GET /api/profile).

import type { EmployerType } from './vacancy'

export type SkillLevel = 'Production' | 'Project' | 'Learning' | 'None'

export type LanguageLevel = 'A1' | 'A2' | 'B1' | 'B2' | 'C1' | 'C2' | 'Native'

export interface ProfileSkill {
  name: string
  aliases: string[]
  level: SkillLevel
  years: number | null
  group: string | null
}

export interface KnownGap {
  skill: string
  aliases: string[]
  de: string
  en: string
}

export interface ExperienceEntry {
  company: string
  title: string
  from: string
  to: string | null
  location: string | null
  remote: boolean
  bullets: { text: string, tags: string[] }[]
}

export interface CandidateProfile {
  personal: {
    name: string
    email: string
    phone: string | null
    city: string | null
    website: string | null
    github: string | null
    linkedin: string | null
  }
  summary: Record<string, string>
  experience: ExperienceEntry[]
  skills: ProfileSkill[]
  knownGaps: KnownGap[]
  projects: { name: string, url: string | null, description: string, tags: string[] }[]
  education: { institution: string, degree: string, from: string, to: string | null, notes: string[] }[]
  languages: { name: string, level: LanguageLevel, note: string | null }[]
  certificates: { name: string, issuer: string | null, date: string | null }[]
  preferences: {
    salaryMin: number | null
    salaryMax: number | null
    regions: string[]
    maxCommuteMinutes: number | null
    remote: 'Remote' | 'Hybrid' | 'OnSite' | 'Any'
    earliestStart: string | null
    avoidEmployerTypes: EmployerType[]
  }
}

export interface ProfileResponse {
  status: 'Loaded' | 'Missing' | 'Invalid'
  fileName: string
  loadedAt: string | null
  errors: string[]
  warnings: string[]
  experienceYears: number | null
  profile: CandidateProfile | null
}
