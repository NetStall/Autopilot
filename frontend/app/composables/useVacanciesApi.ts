import type {
  ConfiguredSearch,
  FitResponse,
  ImportRun,
  ImportStarted,
  VacancyPage,
  RescoreStatus,
  UiSettings,
  Vacancy,
  VacancyInterest,
  VacancyListParams,
} from '~/types/vacancy'
import type { ProfileResponse } from '~/types/profile'
import type {
  SaveSearchProfileResponse,
  SearchProfile,
  SearchProfilePreview,
  SearchProfileResponse,
  SearchProfileRevision,
} from '~/types/searchProfile'
import type { Application, ApplicationStatus, TrackerImportReport } from '~/types/application'
import type { ApplicationDocuments, CvOptions, DocumentKind, DocumentPreview, Letter } from '~/types/documents'

/** All calls to the backend API. Base URL from runtime config (NUXT_PUBLIC_API_BASE). */
export function useVacanciesApi() {
  const config = useRuntimeConfig()
  const api = $fetch.create({ baseURL: config.public.apiBase })

  return {
    list(params: VacancyListParams) {
      return api<VacancyPage>('/api/vacancies', { query: { ...params } })
    },

    get(id: string) {
      return api<Vacancy>(`/api/vacancies/${encodeURIComponent(id)}`)
    },

    /** „Passt zu mir?" — fit check against the loaded profile. */
    fit(id: string) {
      return api<FitResponse>(`/api/vacancies/${encodeURIComponent(id)}/fit`)
    },

    async setSeen(id: string, isSeen: boolean): Promise<void> {
      await api(`/api/vacancies/${encodeURIComponent(id)}`, { method: 'PATCH', body: { isSeen } })
    },

    async setInterest(id: string, interest: VacancyInterest): Promise<void> {
      await api(`/api/vacancies/${encodeURIComponent(id)}`, { method: 'PATCH', body: { interest } })
    },

    async markSeen(ids: string[]): Promise<void> {
      if (ids.length > 0) {
        await api('/api/vacancies/seen', { method: 'POST', body: { ids } })
      }
    },

    /** Queues re-scoring of all vacancies with the current rules („Neu bewerten"). */
    startRescore() {
      return api<RescoreStatus>('/api/vacancies/rescore', { method: 'POST' })
    },

    rescoreStatus() {
      return api<RescoreStatus>('/api/vacancies/rescore')
    },

    /** Queues an import of the configured searches; 409 while one is queued or running. */
    startImport() {
      return api<ImportStarted>('/api/imports', { method: 'POST' })
    },

    getImport(id: string) {
      return api<ImportRun>(`/api/imports/${encodeURIComponent(id)}`)
    },

    recentImports(limit = 10) {
      return api<ImportRun[]>('/api/imports', { query: { limit } })
    },

    searches() {
      return api<ConfiguredSearch[]>('/api/imports/searches')
    },

    listApplications(params: { status?: ApplicationStatus, channel?: string, followUpDue?: boolean }) {
      return api<Application[]>('/api/applications', { query: { ...params } })
    },

    getApplication(id: string) {
      return api<Application>(`/api/applications/${encodeURIComponent(id)}`)
    },

    changeApplicationStatus(id: string, status: ApplicationStatus, note?: string) {
      return api<Application>(`/api/applications/${encodeURIComponent(id)}/status`, {
        method: 'POST',
        body: { status, note: note?.trim() || undefined },
      })
    },

    /** „Als beworben markieren" — returns the existing application if there is one. */
    markApplied(vacancyId: string) {
      return api<Application>(`/api/vacancies/${encodeURIComponent(vacancyId)}/apply`, { method: 'POST' })
    },

    /** Imports data/import/bewerbungen-export.json (idempotent). */
    importTracker() {
      return api<TrackerImportReport>('/api/applications/import-tracker', { method: 'POST' })
    },

    searchProfile() {
      return api<SearchProfileResponse>('/api/search-profile')
    },

    /** 409 when the Suchprofil was saved elsewhere in the meantime. */
    saveSearchProfile(version: number, profile: SearchProfile) {
      return api<SaveSearchProfileResponse>('/api/search-profile', { method: 'PUT', body: { version, profile } })
    },

    /** Scores everything with these settings without saving; also validates. */
    previewSearchProfile(profile: SearchProfile) {
      return api<SearchProfilePreview>('/api/search-profile/preview', { method: 'POST', body: profile })
    },

    searchProfileHistory() {
      return api<SearchProfileRevision[]>('/api/search-profile/history')
    },

    rollbackSearchProfile(version: number, expectedVersion: number) {
      return api<SaveSearchProfileResponse>('/api/search-profile/rollback', { method: 'POST', body: { version, expectedVersion } })
    },

    /** URL of the JSON download. */
    searchProfileExportUrl() {
      return `${config.public.apiBase}/api/search-profile/export`
    },

    /** The loaded candidate profile with its validation problems. */
    profile() {
      return api<ProfileResponse>('/api/profile')
    },

    uiSettings() {
      return api<UiSettings>('/api/settings/ui')
    },

    /** „Unterlagen erstellen" for a vacancy: the existing documents, or a new draft application (one Claude call). */
    createDocumentsForVacancy(vacancyId: string) {
      return api<ApplicationDocuments>(`/api/vacancies/${encodeURIComponent(vacancyId)}/documents`, { method: 'POST' })
    },

    createDocuments(applicationId: string) {
      return api<ApplicationDocuments>(`${documentsPath(applicationId)}`, { method: 'POST' })
    },

    documents(applicationId: string) {
      return api<ApplicationDocuments>(documentsPath(applicationId))
    },

    /** 409 when the documents were changed elsewhere (other tab). */
    saveDocuments(applicationId: string, version: number, letter: Letter, cvOptions: CvOptions) {
      return api<ApplicationDocuments>(documentsPath(applicationId), { method: 'PUT', body: { version, letter, cvOptions } })
    },

    /** „Neu erstellen": a new letter (one Claude call). */
    regenerateLetter(applicationId: string, version: number) {
      return api<ApplicationDocuments>(`${documentsPath(applicationId)}/regenerate`, { method: 'POST', body: { version } })
    },

    /** „Absatz neu formulieren" (0-based index, one Claude call). */
    rephraseParagraph(applicationId: string, version: number, index: number, hint?: string) {
      return api<ApplicationDocuments>(`${documentsPath(applicationId)}/paragraphs/${index}/rephrase`, { method: 'POST', body: { version, hint } })
    },

    restoreDocuments(applicationId: string, version: number, revision: number) {
      return api<ApplicationDocuments>(`${documentsPath(applicationId)}/restore`, { method: 'POST', body: { version, revision } })
    },

    /** „Freigeben": writes the PDFs; with open warnings only with acceptWarnings. */
    approveDocuments(applicationId: string, version: number, acceptWarnings: boolean) {
      return api<ApplicationDocuments>(`${documentsPath(applicationId)}/approve`, { method: 'POST', body: { version, acceptWarnings } })
    },

    /** „Als gesendet markieren" — I submitted the approved documents myself. */
    markSent(applicationId: string, channel: string, at?: string) {
      return api<ApplicationDocuments>(`/api/applications/${encodeURIComponent(applicationId)}/mark-sent`, { method: 'POST', body: { channel, at } })
    },

    /** PNG of page 1 of the unsaved state, as an object URL (revoke it when replaced). */
    async previewDocument(applicationId: string, kind: DocumentKind, letter: Letter, cvOptions: CvOptions, signal?: AbortSignal): Promise<DocumentPreview> {
      const response = await api.raw<Blob>(`${documentsPath(applicationId)}/preview/${kind}`, {
        method: 'POST',
        body: kind === 'letter' ? { letter, cvOptions } : { cvOptions },
        responseType: 'blob',
        signal,
      })
      return {
        url: URL.createObjectURL(response._data as Blob),
        pages: Number(response.headers.get('X-Page-Count') ?? '1'),
      }
    },

    /** Download link of an approved PDF. */
    documentPdfUrl(applicationId: string, kind: DocumentKind) {
      return `${config.public.apiBase}${documentsPath(applicationId)}/${kind}.pdf`
    },
  }
}

function documentsPath(applicationId: string): string {
  return `/api/applications/${encodeURIComponent(applicationId)}/documents`
}

/** Bumped after every finished import so open lists and the header refresh themselves. */
export function useImportVersion() {
  return useState('import-version', () => 0)
}
