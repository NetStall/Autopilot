<script setup lang="ts">
import type { Vacancy } from '~/types/vacancy'
import type { VacancyFilters } from '~/utils/vacancyFilters'

useHead({ title: 'Stellen · Job Autopilot' })

const PAGE_SIZE = 20

const route = useRoute()
const router = useRouter()
const api = useVacanciesApi()
const importVersion = useImportVersion()

const filters = computed(() => filtersFromQuery(route.query))
const listQuery = computed(() => filtersToQuery(filters.value))

const { data, status, error, refresh } = useAsyncData(
  'vacancy-list',
  () => api.list(filtersToApiParams(filters.value, PAGE_SIZE)),
  { watch: [filters, importVersion], server: false, lazy: true },
)

const vacancies = computed(() => data.value?.items ?? [])
const totalCount = computed(() => data.value?.totalCount ?? 0)
const listState = computed(() => resolveListState({
  status: status.value,
  hasError: error.value != null,
  hasData: data.value != null,
  itemCount: vacancies.value.length,
}))
const isRefreshing = computed(() => status.value === 'pending' && data.value != null)
const unseenOnPage = computed(() => vacancies.value.filter(v => !v.isSeen))

const actionError = ref<string | null>(null)
const bulkRunning = ref(false)

function changeFilters(next: VacancyFilters, options: { replace: boolean }) {
  const target = { query: filtersToQuery(next) }
  void (options.replace ? router.replace(target) : router.push(target))
}

function resetFilters() {
  changeFilters({ ...DEFAULT_FILTERS, employerTypes: [], sort: filters.value.sort }, { replace: false })
}

/** data is a shallow ref: replace the page object so the list re-renders. */
function patchVacancy(id: string, patch: Partial<Vacancy>) {
  if (data.value) {
    data.value = { ...data.value, items: data.value.items.map(v => (v.id === id ? { ...v, ...patch } : v)) }
  }
}

async function setSeen(vacancy: Vacancy, isSeen: boolean) {
  actionError.value = null
  patchVacancy(vacancy.id, { isSeen })
  try {
    await api.setSeen(vacancy.id, isSeen)
  }
  catch (e: unknown) {
    patchVacancy(vacancy.id, { isSeen: vacancy.isSeen })
    actionError.value = `Status konnte nicht gespeichert werden: ${describeApiError(e)}`
  }
}

function toggleSeen(vacancy: Vacancy) {
  void setSeen(vacancy, !vacancy.isSeen)
}

function openedOriginal(vacancy: Vacancy) {
  if (!vacancy.isSeen) {
    void setSeen(vacancy, true)
  }
}

async function markPageSeen() {
  actionError.value = null
  bulkRunning.value = true
  try {
    await api.markSeen(unseenOnPage.value.map(v => v.id))
    await refresh()
  }
  catch (e: unknown) {
    actionError.value = `Status konnte nicht gespeichert werden: ${describeApiError(e)}`
  }
  finally {
    bulkRunning.value = false
  }
}
</script>

<template>
  <main class="page">
    <div class="page__header">
      <h1>Stellen</h1>
      <p v-if="data" class="page__count" aria-live="polite">
        {{ totalCount }} {{ totalCount === 1 ? 'Stelle' : 'Stellen' }}
      </p>
    </div>

    <VacancyFilters :filters="filters" @change="changeFilters" />

    <p v-if="actionError" class="page__action-error" role="alert">
      {{ actionError }}
    </p>

    <section :aria-busy="listState === 'loading' || isRefreshing" aria-label="Stellenliste">
      <VacancyListSkeleton v-if="listState === 'loading'" />

      <div v-else-if="listState === 'error'" class="state state--error" role="alert">
        <p>Die Stellen konnten nicht geladen werden. {{ describeApiError(error) }}</p>
        <button type="button" class="button" @click="refresh()">
          Erneut versuchen
        </button>
      </div>

      <div v-else-if="listState === 'empty'" class="state">
        <p><strong>Keine Stellen gefunden.</strong></p>
        <template v-if="hasActiveFilters(filters)">
          <p>Passe die Filter an oder setze sie zurück.</p>
          <button type="button" class="button button--secondary" @click="resetFilters">
            Filter zurücksetzen
          </button>
        </template>
        <p v-else>
          Starte oben einen Import, um Stellen der Bundesagentur für Arbeit zu laden.
        </p>
      </div>

      <template v-else>
        <div class="page__bulk">
          <button
            type="button"
            class="button button--secondary button--small"
            :disabled="unseenOnPage.length === 0 || bulkRunning"
            @click="markPageSeen"
          >
            Alle auf dieser Seite als gesehen markieren
          </button>
        </div>
        <VacancyList
          :class="{ 'page__list--refreshing': isRefreshing }"
          :vacancies="vacancies"
          :list-query="listQuery"
          @toggle-seen="toggleSeen"
          @opened-original="openedOriginal"
        />
        <VacancyPagination :filters="filters" :page-size="PAGE_SIZE" :total-count="totalCount" />
      </template>
    </section>
  </main>
</template>

<style scoped lang="scss">
.page {
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);

  &__header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-3);
    margin-bottom: var(--space-4);

    h1 {
      margin: 0;
      font-size: 1.6rem;
    }
  }

  &__count {
    margin: 0;
    color: var(--color-muted);
  }

  &__action-error {
    color: var(--color-error-text);
  }

  &__bulk {
    display: flex;
    justify-content: flex-end;
    margin-bottom: var(--space-3);
  }

  &__list--refreshing {
    opacity: 0.6;
    transition: opacity 0.15s;
  }
}
</style>
