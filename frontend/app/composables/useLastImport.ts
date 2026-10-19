/** The latest successful import (header: „Letzter Import: vor 2 Std.", „N neu seit letztem Import"). */
export function useLastImport() {
  const api = useVacanciesApi()
  const importVersion = useImportVersion()

  const { data, refresh } = useAsyncData('recent-imports', () => api.recentImports(10), {
    server: false,
    lazy: true,
    watch: [importVersion],
  })

  const lastImport = computed(() => lastSuccessfulImport(data.value ?? []))
  const activeImport = computed(() => (data.value ?? []).find(r => isActiveImport(r.status)) ?? null)

  return { lastImport, activeImport, refresh }
}
