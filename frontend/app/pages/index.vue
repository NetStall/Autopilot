<script setup lang="ts">
import type { Vacancy, VacancyInterest } from '~/types/vacancy'
import type { VacancyFilters } from '~/utils/vacancyFilters'

useHead({ title: 'Stellen · Job Autopilot' })

const PAGE_SIZE = 20

const route = useRoute()
const router = useRouter()
const api = useVacanciesApi()
const importVersion = useImportVersion()
const toasts = useToasts()

const filters = computed(() => filtersFromQuery(route.query))
const listQuery = computed(() => filtersToQuery(filters.value))

const { data, status, error, refresh } = useAsyncData(
  'vacancy-list',
  () => api.list(filtersToApiParams(filters.value, PAGE_SIZE)),
  { watch: [filters, importVersion], server: false, lazy: true },
)

const { data: searches } = useAsyncData('import-searches', () => api.searches(), { server: false, lazy: true })
const { data: uiSettings } = useAsyncData('ui-settings', () => api.uiSettings(), { server: false, lazy: true })
const highlightKeywords = computed(() => uiSettings.value?.highlightKeywords ?? [])

// Dismissed vacancies leave the default list at once instead of on the next reload.
const vacancies = computed(() => (data.value?.items ?? [])
  .filter(v => filters.value.showDismissed || filters.value.starredOnly || v.interest !== 'Dismissed'))
const totalCount = computed(() => data.value?.totalCount ?? 0)
const listState = computed(() => resolveListState({
  status: status.value,
  hasError: error.value != null,
  hasData: data.value != null,
  itemCount: vacancies.value.length,
}))
const emptyState = computed(() => emptyStateFor(filters.value))
const isRefreshing = computed(() => status.value === 'pending' && data.value != null)
const unseenOnPage = computed(() => vacancies.value.filter(v => !v.isSeen))
const bulkRunning = ref(false)

// ---------------------------------------------------------------- filters (URL + remembered per browser)
function changeFilters(next: VacancyFilters, options: { replace: boolean }) {
  const target = { query: filtersToQuery(next) }
  void (options.replace ? router.replace(target) : router.push(target))
}

function resetFilters() {
  changeFilters({ ...defaultFilters(), sort: filters.value.sort }, { replace: false })
}

// First restore (only when / is opened without filters), then remember every state from now on — including the
// one the page was opened with (a link or reload with ?remote=true).
onMounted(() => {
  if (Object.keys(route.query).length === 0) {
    const saved = loadSavedFilters(browserStorage())
    if (saved) {
      void router.replace({ query: saved })
    }
  }
  watch(() => route.query, query => saveFilters(browserStorage(), query), { immediate: true })
})

// ---------------------------------------------------------------- optimistic actions
/** data is a shallow ref: replace the page object so the list re-renders. */
function patchVacancy(id: string, patch: Partial<Vacancy>) {
  if (data.value) {
    data.value = { ...data.value, items: data.value.items.map(v => (v.id === id ? { ...v, ...patch } : v)) }
  }
}

function saveFailed(error: unknown) {
  toasts.error(`Änderung konnte nicht gespeichert werden: ${describeApiError(error)}`)
}

function setSeen(vacancy: Vacancy, isSeen: boolean) {
  return runOptimistic({
    apply: () => patchVacancy(vacancy.id, { isSeen }),
    rollback: () => patchVacancy(vacancy.id, { isSeen: vacancy.isSeen }),
    request: () => api.setSeen(vacancy.id, isSeen),
    onError: saveFailed,
  })
}

function setInterest(vacancy: Vacancy, interest: VacancyInterest) {
  return runOptimistic({
    apply: () => patchVacancy(vacancy.id, { interest }),
    rollback: () => patchVacancy(vacancy.id, { interest: vacancy.interest }),
    request: () => api.setInterest(vacancy.id, interest),
    onError: saveFailed,
  })
}

const toggleSeen = (vacancy: Vacancy) => void setSeen(vacancy, !vacancy.isSeen)
const toggleStar = (vacancy: Vacancy) => void setInterest(vacancy, vacancy.interest === 'Starred' ? 'None' : 'Starred')
const toggleDismiss = (vacancy: Vacancy) =>
  void setInterest(vacancy, vacancy.interest === 'Dismissed' ? 'None' : 'Dismissed')

function openedOriginal(vacancy: Vacancy) {
  if (!vacancy.isSeen) {
    void setSeen(vacancy, true)
  }
}

async function markPageSeen() {
  bulkRunning.value = true
  try {
    await api.markSeen(unseenOnPage.value.map(v => v.id))
    await refresh()
  }
  catch (e: unknown) {
    saveFailed(e)
  }
  finally {
    bulkRunning.value = false
  }
}

// ---------------------------------------------------------------- keyboard shortcuts
const activeId = ref<string | null>(null)
const helpOpen = ref(false)
const activeVacancy = computed(() => vacancies.value.find(v => v.id === activeId.value) ?? null)

function focusCard(id: string) {
  activeId.value = id
  void nextTick(() => {
    const card = document.getElementById(`card-${id}`)
    card?.focus({ preventScroll: true })
    card?.scrollIntoView({ block: 'nearest' })
  })
}

function onKeydown(event: KeyboardEvent) {
  const action = shortcutFor(event)
  if (!action || listState.value !== 'ready' && action !== 'help' && action !== 'close') {
    return
  }
  if (helpOpen.value && action !== 'close' && action !== 'help') {
    return
  }

  const list = vacancies.value
  const current = list.findIndex(v => v.id === activeId.value)
  const vacancy = activeVacancy.value

  switch (action) {
    case 'next':
    case 'previous': {
      const index = moveActiveIndex(current, list.length, action)
      if (index >= 0) {
        focusCard(list[index]!.id)
      }
      break
    }
    case 'open': {
      const url = safeExternalUrl(vacancy?.url)
      if (vacancy && url) {
        window.open(url, '_blank', 'noopener,noreferrer')
        openedOriginal(vacancy)
      }
      break
    }
    case 'toggleSeen':
      if (vacancy) toggleSeen(vacancy)
      break
    case 'toggleStar':
      if (vacancy) toggleStar(vacancy)
      break
    case 'toggleDismiss':
      if (vacancy) {
        // Keep the keyboard flow: move on before the card disappears from the list.
        const nextVacancy = list[current + 1] ?? list[current - 1]
        toggleDismiss(vacancy)
        if (nextVacancy && !filters.value.showDismissed && vacancy.interest !== 'Dismissed') {
          focusCard(nextVacancy.id)
        }
      }
      break
    case 'help':
      helpOpen.value = !helpOpen.value
      break
    case 'close':
      helpOpen.value = false
      return
  }
  event.preventDefault()
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <main class="page">
    <div class="page__header">
      <h1>Stellen</h1>
      <p class="page__meta">
        <span v-if="data" aria-live="polite">{{ totalCount }} {{ totalCount === 1 ? 'Stelle' : 'Stellen' }}</span>
        <RescoreButton @finished="refresh()" />
        <button type="button" class="page__help" @click="helpOpen = true">
          Tastenkürzel <kbd>?</kbd>
        </button>
      </p>
    </div>

    <VacancyFilters :filters="filters" :searches="searches ?? []" @change="changeFilters" />

    <section :aria-busy="listState === 'loading' || isRefreshing" aria-label="Stellenliste">
      <VacancyListSkeleton v-if="listState === 'loading'" />

      <div v-else-if="listState === 'error'" class="state state--error" role="alert">
        <p>Die Stellen konnten nicht geladen werden. {{ describeApiError(error) }}</p>
        <button type="button" class="button" @click="refresh()">
          Erneut versuchen
        </button>
      </div>

      <div v-else-if="listState === 'empty'" class="state">
        <p><strong>{{ emptyState.title }}</strong></p>
        <p>{{ emptyState.hint }}</p>
        <button v-if="emptyState.canReset" type="button" class="button button--secondary" @click="resetFilters">
          Filter zurücksetzen
        </button>
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
          :highlight-keywords="highlightKeywords"
          :active-id="activeId"
          @toggle-seen="toggleSeen"
          @toggle-star="toggleStar"
          @toggle-dismiss="toggleDismiss"
          @opened-original="openedOriginal"
          @activate="v => (activeId = v.id)"
        />
        <VacancyPagination :filters="filters" :page-size="PAGE_SIZE" :total-count="totalCount" />
      </template>
    </section>

    <ShortcutHelp v-model:open="helpOpen" />
  </main>
</template>

<style scoped lang="scss">
.page {
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);

  &__header {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-3);
    margin-bottom: var(--space-4);

    h1 {
      margin: 0;
      font-size: 1.6rem;
    }
  }

  &__meta {
    display: flex;
    align-items: baseline;
    gap: var(--space-4);
    margin: 0;
    color: var(--color-muted);
  }

  &__help {
    font: inherit;
    font-size: 0.85rem;
    border: 0;
    background: transparent;
    color: var(--color-accent);
    cursor: pointer;

    kbd {
      padding: 0 0.35em;
      border: 1px solid var(--color-border);
      border-radius: 4px;
    }
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
