import type {
  ConfiguredSearch,
  FitResponse,
  ImportRun,
  ImportStarted,
  PagedResponse,
  RescoreStatus,
  UiSettings,
  Vacancy,
  VacancyInterest,
  VacancyListParams,
} from '~/types/vacancy'
import type { ProfileResponse } from '~/types/profile'
import type { Application, ApplicationStatus, TrackerImportReport } from '~/types/application'

/** All calls to the backend API. Base URL from runtime config (NUXT_PUBLIC_API_BASE). */
export function useVacanciesApi() {
  const config = useRuntimeConfig()
  const api = $fetch.create({ baseURL: config.public.apiBase })

  return {
    list(params: VacancyListParams) {
      return api<PagedResponse<Vacancy>>('/api/vacancies', { query: { ...params } })
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

    /** The loaded candidate profile with its validation problems. */
    profile() {
      return api<ProfileResponse>('/api/profile')
    },

    uiSettings() {
      return api<UiSettings>('/api/settings/ui')
    },
  }
}

/** Bumped after every finished import so open lists and the header refresh themselves. */
export function useImportVersion() {
  return useState('import-version', () => 0)
}
