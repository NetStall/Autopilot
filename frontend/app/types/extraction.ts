/** The model's reading of one posting (step 07a); names as the model wrote them. */
export interface RequirementExtraction {
  mustHave: string[]
  niceToHave: string[]
  germanLevel: string | null
  germanNotRequired: boolean
  englishLevel: string | null
  minYears: number | null
  seniority: 'Unknown' | 'Junior' | 'Mid' | 'Senior' | 'Lead'
  remoteLevel: 'Unknown' | 'OnSite' | 'Hybrid' | 'FullRemote'
  homeOfficePercent: number | null
  employmentType: 'Unknown' | 'FullTime' | 'PartTime' | 'Contract' | 'Internship' | 'WorkingStudent'
  domainRequirements: string[]
  summary: string
  homeOfficeDaysPerWeek?: number | null
  softSkills?: string[] | null
}

/** „KI-Bewertung" (07b): the local model's judgement of the vacancy against my profile. */
export interface AiFitScore {
  score: number
  verdict: 'passt' | 'eher nicht' | 'passt nicht'
  reasons: string[]
  blockers: string[]
}

/** A short name for a chip and the posting's own words it came from (tooltip). */
export interface ExtractionChip {
  name: string
  sourceText: string
}

export interface ExtractionChips {
  mustHave: ExtractionChip[]
  niceToHave: ExtractionChip[]
  domains: ExtractionChip[]
  /** Personal qualities: shown greyed out, never scored. */
  softSkills: string[]
}

/** On the vacancy detail: the extraction for the current description. */
export interface VacancyExtraction {
  result: RequirementExtraction
  model: string
  promptVersion: string
  extractedAt: string
  chips?: ExtractionChips | null
}

/** GET /api/extractions — the background analysis. */
export interface ExtractionStatus {
  state: 'Idle' | 'Queued' | 'Running'
  /** The task's provider is usable (Ollama on, model mapped). */
  available: boolean
  model: string
  total: number
  done: number
  extracted: number
  cached: number
  failed: number
  currentVacancyId: string | null
  queued: string[]
  secondsPerVacancy: number | null
  tokensPerSecond: number | null
  /** s/vacancy of the last measured run — for the estimate before starting. */
  lastSecondsPerVacancy: number | null
  startedAt: string | null
  finishedAt: string | null
  lastError: string | null
}

/** GET /api/extractions/pending?top=N — what a run of the current Top N would do. */
export interface PendingExtractions {
  top: number
  pending: number
  secondsPerVacancy: number
  estimatedSeconds: number
}

/** GET /api/extractions/failed — „ungültig": invalid twice or timed out. */
export interface FailedExtraction {
  vacancyId: string
  title: string
  company: string
  rankScore: number | null
  error: string | null
  failedAt: string
}

/** GET /api/labels/summary — „Bewertet: 12 / 30". */
export interface GoldLabelSummary {
  labeled: number
  fits: number
  notFits: number
  target: number
}
