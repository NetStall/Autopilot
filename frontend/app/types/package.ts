// Mirrors JobAutopilot.Api.Features.Packages and the follow-ups (08c). Nothing is ever sent from the tool.
import type { DraftWarning } from './documents'
import type { ApplyChannel, HowToApply } from './statistics'
import type { ApplicationStatus } from './application'

export interface ChecklistItem {
  label: string
  value: string | null
}

export interface ApplicationPackage {
  applicationId: string
  vacancyId: string | null
  company: string
  position: string
  status: ApplicationStatus
  statusLabel: string
  approved: boolean
  howToApply: HowToApply
  email: { recipient: string | null, subject: string, salutation: string, body: string, closing: string, senderName: string, attachments: string[] }
  portal: { checklist: ChecklistItem[], motivation: string, aboutMe: string, postingUrl: string | null }
  warnings: DraftWarning[]
  letterWarnings: DraftWarning[]
  generatedAt: string
  version: number
}

export interface ReadyPackage {
  applicationId: string
  vacancyId: string | null
  company: string
  position: string
  status: ApplicationStatus
  channel: ApplyChannel
  email: string | null
  approved: boolean
  warnings: number
  generatedAt: string
}

export interface PackageEstimate {
  k: number
  candidates: { vacancyId: string, title: string, company: string, rankScore: number | null }[]
  calls: number
  costUsd: number
  remainingCalls: number
  withinLimit: boolean
}

export interface PackageResult {
  vacancyId: string
  title: string
  applicationId: string | null
  error: string | null
}

export interface FollowUp {
  applicationId: string
  company: string
  position: string
  status: ApplicationStatus
  statusLabel: string
  appliedAt: string
  lastActionAt: string
  dueAt: string
  draft: { subject: string, email: string, linkedIn: string }
}
