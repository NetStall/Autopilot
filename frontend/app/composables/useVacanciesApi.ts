import type { ImportResult, PagedResponse, Vacancy, VacancyListParams } from '~/types/vacancy'

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

    async markSeen(ids: string[]): Promise<void> {
      if (ids.length > 0) {
        await api('/api/vacancies/seen', { method: 'POST', body: { ids } })
      }
    },

    runImport() {
      return api<ImportResult>('/api/imports/arbeitsagentur', { method: 'POST' })
    },
  }
}

/** Bumped after every successful import so open lists can refresh themselves. */
export function useImportVersion() {
  return useState('import-version', () => 0)
}
