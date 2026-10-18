<script setup lang="ts">
import type { SearchImportResult } from '~/types/vacancy'

const api = useVacanciesApi()
const importVersion = useImportVersion()

const running = ref(false)
const summary = ref<string | null>(null)
const searchResults = ref<SearchImportResult[]>([])
const errorMessage = ref<string | null>(null)

function searchDescription(search: SearchImportResult): string {
  const where = search.remoteOnly ? 'remote, Deutschland' : (search.location ?? 'Deutschland')
  return `${search.keyword} · ${where}`
}

async function startImport() {
  running.value = true
  summary.value = null
  searchResults.value = []
  errorMessage.value = null
  try {
    const result = await api.runImport()
    summary.value = formatImportSummary(result)
    searchResults.value = result.searches
    importVersion.value++
  }
  catch (error: unknown) {
    errorMessage.value = `Import fehlgeschlagen: ${describeApiError(error)}`
  }
  finally {
    running.value = false
  }
}
</script>

<template>
  <div class="import">
    <button type="button" class="button" :disabled="running" :aria-busy="running" @click="startImport">
      {{ running ? 'Import läuft …' : 'Import starten' }}
    </button>
    <p class="import__status" aria-live="polite">
      <span v-if="summary">{{ summary }}</span>
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
    <p v-if="errorMessage" class="import__error" role="alert">
      {{ errorMessage }}
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

  &__note {
    max-width: 36rem;
    margin: var(--space-2) 0 0 auto;
    color: var(--color-muted);
    text-align: right;
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

  &__error {
    flex-basis: 100%;
    text-align: right;
    color: var(--color-error-text);
  }
}

@media (max-width: 560px) {
  .import {
    justify-content: flex-start;

    &__error {
      text-align: left;
    }
  }
}
</style>
