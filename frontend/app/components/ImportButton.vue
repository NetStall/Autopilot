<script setup lang="ts">
import type { ImportRun, SearchImportResult } from '~/types/vacancy'

const POLL_INTERVAL_MS = 1000

const api = useVacanciesApi()
const importVersion = useImportVersion()
const { activeImport } = useLastImport()

const run = ref<ImportRun | null>(null)
const starting = ref(false)
const errorMessage = ref<string | null>(null)
let pollTimer: ReturnType<typeof setTimeout> | undefined

const view = computed(() => (run.value ? importProgressView(run.value) : null))
const busy = computed(() => starting.value || (view.value?.active ?? false))
const searchResults = computed<SearchImportResult[]>(() =>
  run.value?.status === 'Succeeded' && Array.isArray(run.value.searches) ? run.value.searches : [])

function searchDescription(search: SearchImportResult): string {
  const where = search.remoteOnly ? 'remote, Deutschland' : (search.location ?? 'Deutschland')
  return `${search.keyword} · ${where}`
}

async function poll(importId: string) {
  clearTimeout(pollTimer)
  try {
    run.value = await api.getImport(importId)
  }
  catch (error: unknown) {
    errorMessage.value = `Importstatus nicht abrufbar: ${describeApiError(error)}`
    return
  }
  if (isActiveImport(run.value.status)) {
    pollTimer = setTimeout(() => void poll(importId), POLL_INTERVAL_MS)
  }
  else {
    importVersion.value++ // refreshes the list and the header
  }
}

async function startImport() {
  starting.value = true
  errorMessage.value = null
  try {
    const started = await api.startImport()
    run.value = null
    await poll(started.importId)
  }
  catch (error: unknown) {
    if (errorStatus(error) === 409) {
      // Already running (e.g. the daily import): follow that one instead of failing.
      const running = (await api.recentImports(5).catch(() => [])).find(r => isActiveImport(r.status))
      if (running) {
        await poll(running.id)
        return
      }
    }
    errorMessage.value = `Import fehlgeschlagen: ${describeApiError(error)}`
  }
  finally {
    starting.value = false
  }
}

// A scheduled import that is already running when the page opens is shown too.
watch(activeImport, (active) => {
  if (active && !busy.value) {
    void poll(active.id)
  }
}, { immediate: true })

onBeforeUnmount(() => clearTimeout(pollTimer))
</script>

<template>
  <div class="import">
    <button type="button" class="button" :disabled="busy" :aria-busy="busy" @click="startImport">
      {{ busy ? 'Import läuft …' : 'Import starten' }}
    </button>

    <div v-if="view?.active" class="import__progress">
      <progress :value="view.percent" max="100" aria-label="Importfortschritt" />
      <span class="import__label" aria-live="polite">{{ view.label }}</span>
    </div>
    <p class="import__status" aria-live="polite">
      <span v-if="view?.summary">{{ view.summary }}</span>
    </p>

    <details v-if="searchResults.length > 0" class="import__details">
      <summary>Details je Suche</summary>
      <div class="import__table-wrap">
        <table class="import__table">
          <caption class="visually-hidden">
            Ergebnis je Suche
          </caption>
          <thead>
            <tr>
              <th scope="col">
                Suche
              </th>
              <th scope="col">
                Gefunden
              </th>
              <th scope="col">
                Neu
              </th>
              <th scope="col">
                Aktualisiert
              </th>
              <th scope="col">
                Unverändert
              </th>
              <th scope="col">
                Ausgeschlossen
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="search in searchResults" :key="search.name">
              <th scope="row" :title="searchDescription(search)">
                {{ search.name }}
              </th>
              <td>{{ search.fetched }}</td>
              <td>{{ search.added }}</td>
              <td>{{ search.updated }}</td>
              <td>{{ search.unchanged }}</td>
              <td>{{ search.excluded }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="import__note">
        Jede Stelle zählt einmal – bei der ersten Suche, die sie gefunden hat. „Gefunden" enthält auch Treffer,
        die schon eine frühere Suche geliefert hat.
      </p>
    </details>

    <p v-if="errorMessage || view?.error" class="import__error" role="alert">
      {{ errorMessage ?? view?.error }}
    </p>
  </div>
</template>

<style scoped lang="scss">
.import {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: var(--space-2) var(--space-3);

  &__progress {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    font-size: 0.9rem;

    progress {
      width: 9rem;
      accent-color: var(--color-accent);
    }
  }

  &__label {
    color: var(--color-muted);
  }

  &__status,
  &__error {
    margin: 0;
    font-size: 0.9rem;
  }

  &__status {
    color: var(--color-muted);

    &:empty {
      display: none;
    }
  }

  &__details {
    flex-basis: 100%;
    font-size: 0.85rem;

    summary {
      cursor: pointer;
      text-align: right;
      color: var(--color-accent);
    }
  }

  &__table-wrap {
    overflow-x: auto;
  }

  &__table {
    margin: var(--space-2) 0 0 auto;
    border-collapse: collapse;
    background: var(--color-surface);

    th,
    td {
      padding: var(--space-1) var(--space-2);
      border-bottom: 1px solid var(--color-border);
      text-align: right;
      white-space: nowrap;
    }

    th[scope='row'],
    thead th:first-child {
      text-align: left;
    }
  }

  &__note {
    max-width: 36rem;
    margin: var(--space-2) 0 0 auto;
    color: var(--color-muted);
    text-align: right;
  }

  &__error {
    flex-basis: 100%;
    text-align: right;
    color: var(--color-error-text);
  }
}

@media (max-width: 560px) {
  .import {
    justify-content: flex-start;

    &__details summary,
    &__note,
    &__error {
      text-align: left;
    }

    &__table,
    &__note {
      margin-left: 0;
    }
  }
}
</style>
