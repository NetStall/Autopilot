// Mirrors JobAutopilot.Api.Features.Applications.

export type ApplicationStatus =
  | 'Draft' | 'Approved' | 'Sent' | 'Acknowledged' | 'Response' | 'Interview' | 'TechInterview'
  | 'Offer' | 'Rejected' | 'NoResponse' | 'Withdrawn'

export interface ApplicationEvent {
  at: string
  from: ApplicationStatus | null
  to: ApplicationStatus
  toLabel: string
  note: string | null
}

export interface Application {
  id: string
  vacancyId: string | null
  company: string
  position: string
  location: string | null
  channel: string | null
  contactName: string | null
  contactEmail: string | null
  url: string | null
  appliedAt: string
  lastActionAt: string
  status: ApplicationStatus
  statusLabel: string
  isOpen: boolean
  followUpAt: string | null
  isFollowUpDue: boolean
  salaryExpectation: number | null
  documents: string[]
  nextStep: string | null
  notes: string | null
  externalRef: string | null
  createdAt: string
  updatedAt: string
  /** Only in the detail response. */
  events: ApplicationEvent[] | null
}

/** POST /api/applications/import-tracker */
export interface TrackerImportReport {
  total: number
  imported: number
  updated: number
  unchanged: number
  linkedToVacancy: number
  warnings: string[]
}

/** On a vacancy: I already applied (to it or to the same company with a similar title). */
export interface AppliedInfo {
  applicationId: string
  status: ApplicationStatus
  statusLabel: string
  appliedAt: string
}
