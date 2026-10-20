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
