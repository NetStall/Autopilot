// Mirrors JobAutopilot.Api.Features.Statistics (GET /api/statistics/german).

import type { RequiredGerman } from './vacancy'

export interface GermanLevelCount {
  level: RequiredGerman
  label: string
  count: number
  percent: number
}

export interface GermanBreakdown {
  key: string
  total: number
  levels: GermanLevelCount[]
}

export interface GermanStatistics {
  minScore: number
  total: number
  overall: GermanLevelCount[]
  byRemoteLevel: GermanBreakdown[]
  bySource: GermanBreakdown[]
}

// GET /api/statistics/channels — „Bewerbungswege" (08a).
export type ApplyChannel = 'Unknown' | 'Email' | 'Portal' | 'Form' | 'External'
export type ApplySource = 'None' | 'Field' | 'Rule' | 'Ai'

export interface ChannelCount {
  channel: ApplyChannel
  count: number
  percent: number
}

export interface EmailReadyVacancy {
  vacancyId: string
  title: string
  company: string
  rankScore: number | null
  email: string
  contactName: string | null
  source: ApplySource
}

export interface ChannelComparison {
  withAi: number
  sameChannel: number
  bothSameEmail: number
  rulesOnlyEmail: number
  aiOnlyEmail: number
  differentEmail: number
  aiEmailRejected: number
}

export interface ChannelStatistics {
  top: number
  vacancies: number
  channels: ChannelCount[]
  emailReady: number
  emailReadyPercent: number
  topEmailReady: EmailReadyVacancy[]
  comparison: ChannelComparison
}

/** How to apply to one vacancy (detail page): only shown, never contacted. */
export interface HowToApply {
  channel: ApplyChannel
  email: string | null
  contactName: string | null
  salutation: string | null
  emailAllowed: boolean
  portalOnly: boolean
  requested: string[]
  source: ApplySource
  externalUrl: string | null
}

/** GET /api/statistics/filters — „Harte Filter" (08b). */
export interface FilterStatistics {
  vacancies: number
  filtered: number
  reasons: { reason: string, label: string, count: number }[]
  exempt: number
  secondsPerVacancy: number
  savedSeconds: number
}

// „Tageslauf" (08b): GET/POST /api/daily-run, GET /api/statistics/coverage.
export type DailyRunStage = 'Import' | 'Filters' | 'Scoring' | 'Extraction' | 'Channels' | 'Summary'
export type StageOutcome = 'Done' | 'Skipped' | 'Failed' | 'Cancelled'

export interface DailyRunStageResult {
  stage: DailyRunStage
  outcome: StageOutcome
  message: string
  durationMs: number
}

export interface DailyRunSummary {
  startedAt: string
  finishedAt: string
  newVacancies: number
  newFiltered: Record<string, number>
  newInTop20: { vacancyId: string, title: string, company: string, rankScore: number | null }[]
  emailReadyInTop20: { vacancyId: string, title: string, company: string, email: string, contactName: string | null }[]
  failures: string[]
  stages: DailyRunStageResult[]
}

export interface DailyRunStatus {
  state: 'Idle' | 'Queued' | 'Running'
  currentStage: DailyRunStage | null
  stages: DailyRunStageResult[]
  last: DailyRunSummary | null
}

export interface SearchCoverage {
  name: string
  source: string | null
  configured: boolean
  vacancies: number
  inTop50: number
}

export interface Coverage {
  days: number
  vacancies: number
  searches: SearchCoverage[]
  bringNothing: string[]
}
