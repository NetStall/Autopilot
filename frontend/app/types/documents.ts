// Mirrors JobAutopilot.Api.Features.Documents (GET /api/applications/{id}/documents).

import type { ApplicationStatus } from './application'

export type DocumentLanguage = 'De' | 'En'
export type Formality = 'Sie' | 'Du'
export type WarningSeverity = 'Warning' | 'Hint'
export type DraftRevisionKind = 'Generated' | 'Rephrased' | 'Saved' | 'Restored'
export type DocumentKind = 'letter' | 'cv'

export interface Letter {
  subject: string
  salutation: string
  paragraphs: string[]
  closing: string
  formality: Formality
  language: DocumentLanguage
  factsUsed: string[]
  gapsMentioned: string[]
}

export interface CvOptions {
  focus: string | null
  hiddenBullets: string[]
  shownBullets: string[]
  bulletOrder: string[]
  hiddenSkillGroups: string[]
  skillGroupOrder: string[]
}

export interface CvBullet {
  id: string
  text: string
  score: number
  hidden: boolean
  /** Hidden by the builder (limit per entry / one page), not by me. */
  autoHidden: boolean
}

export interface CvExperience {
  id: string
  title: string
  company: string
  where: string | null
  period: string
  bullets: CvBullet[]
}

export interface CvSkillGroup {
  name: string
  skills: string[]
  hidden: boolean
}

export interface Cv {
  language: DocumentLanguage
  focus: string
  availableFocuses: string[]
  roleLine: string
  summary: string | null
  experience: CvExperience[]
  skillGroups: CvSkillGroup[]
  untranslatedTexts: number
}

export interface DraftWarning {
  code: string
  message: string
  /** 0-based paragraph of the letter, if the warning is about one. */
  paragraph: number | null
  severity: WarningSeverity
}

export interface CheatSheetItem {
  key: string
  label: string
  value: string
  multiline: boolean
}

export interface DraftUsage {
  inputTokens: number
  outputTokens: number
  calls: number
  costUsd: number
  tokenBudget: number
  callsToday: number
  dailyCallLimit: number
}

export interface DraftRevision {
  number: number
  kind: DraftRevisionKind
  createdAt: string
  note: string | null
  model: string | null
  inputTokens: number
  outputTokens: number
}

export interface ApplicationDocuments {
  applicationId: string
  vacancyId: string | null
  company: string
  position: string
  status: ApplicationStatus
  statusLabel: string
  version: number
  letter: Letter
  cvOptions: CvOptions
  cv: Cv
  warnings: DraftWarning[]
  hasOpenWarnings: boolean
  approvedAt: string | null
  warningsAccepted: boolean
  letterFile: string | null
  cvFile: string | null
  letterPages: number
  cvPages: number
  model: string
  promptVersion: string
  usage: DraftUsage
  claudeConfigured: boolean
  cheatSheet: CheatSheetItem[]
  revisions: DraftRevision[]
  updatedAt: string
}

export interface DocumentPreview {
  url: string
  pages: number
}
