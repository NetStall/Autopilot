import type {
  ConfiguredSearch,
  ImportRun,
  ImportStarted,
  PagedResponse,
  RescoreStatus,
  UiSettings,
  Vacancy,
  VacancyInterest,
  VacancyListParams,
} from '~/types/vacancy'

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

    uiSettings() {
      return api<UiSettings>('/api/settings/ui')
    },
  }
}

/** Bumped after every finished import so open lists and the header refresh themselves. */
export function useImportVersion() {
  return useState('import-version', () => 0)
}
