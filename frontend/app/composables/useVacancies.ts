import type { PagedResponse, Vacancy } from '~/types/vacancy'

/** Loads one page of vacancies from the backend (client-side, so the loading state is visible). */
export function useVacancies(page = 1, pageSize = 20) {
  const config = useRuntimeConfig()

  return useFetch<PagedResponse<Vacancy>>('/api/vacancies', {
    baseURL: config.public.apiBase,
    query: { page, pageSize },
    server: false,
    lazy: true,
  })
}
