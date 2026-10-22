<script setup lang="ts">
import type { GermanBreakdown, GermanLevelCount } from '~/types/statistics'

useHead({ title: 'Statistik · Job Autopilot' })

const api = useVacanciesApi()
const { data, status, error, refresh } = useAsyncData('statistics-german', () => api.germanStatistics(), { server: false, lazy: true })

function percent(value: number): string {
  return `${value.toLocaleString('de-DE', { maximumFractionDigits: 1 })} %`
}

function cell(levels: GermanLevelCount[], level: GermanLevelCount['level']): GermanLevelCount | undefined {
  return levels.find(l => l.level === level)
}

function breakdownLabel(kind: 'remote' | 'source', row: GermanBreakdown): string {
  return kind === 'remote' ? remoteLevelLabel(row.key as Parameters<typeof remoteLevelLabel>[0]) : sourceLabel(row.key as Parameters<typeof sourceLabel>[0])
}

/** Link to the list with this German filter (and the breakdown's own filter). */
function listLink(level: GermanLevelCount['level'], kind?: 'remote' | 'source', key?: string) {
  const query: Record<string, string> = { german: level }
  if (kind === 'source' && key) {
    query.source = key
  }
  if (kind === 'remote' && key === 'FullRemote') {
    query.remote = 'full'
  }
  return { path: '/', query }
}
</script>

<template>
  <main class="stats">
    <div class="stats__header">
      <h1>Statistik</h1>
      <button type="button" class="button button--secondary button--small" @click="refresh()">
        Aktualisieren
      </button>
    </div>
    <p class="stats__hint">
      Welches Deutsch verlangen die relevanten Stellen? „Relevant" = Passt oder Vielleicht, Bewertung mindestens
      {{ data?.minScore ?? '…' }} (Mindestbewertung aus dem Suchprofil), nicht ausgeblendet.
    </p>

    <div v-if="status === 'idle' || status === 'pending'" class="state" role="status">
      Statistik wird geladen …
    </div>
    <div v-else-if="error" class="state state--error" role="alert">
      Die Statistik konnte nicht geladen werden. {{ describeApiError(error) }}
    </div>

    <template v-else-if="data">
      <section aria-labelledby="overall-heading" class="stats__section">
        <h2 id="overall-heading">
          Deutsch erforderlich – alle {{ data.total }} relevanten Stellen
        </h2>
        <table class="stats__table">
          <caption class="visually-hidden">
            Verteilung nach gefordertem Deutsch
          </caption>
          <thead>
            <tr>
              <th scope="col">
                Deutsch
              </th>
              <th scope="col" class="stats__number">
                Stellen
              </th>
              <th scope="col" class="stats__number">
                Anteil
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in data.overall" :key="row.level">
              <th scope="row">
                <NuxtLink :to="listLink(row.level)">
                  {{ row.label }}
                </NuxtLink>
              </th>
              <td class="stats__number">
                {{ row.count }}
              </td>
              <td class="stats__number">
                {{ percent(row.percent) }}
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section
        v-for="table in [{ kind: 'remote' as const, title: 'Nach Arbeitsort', rows: data.byRemoteLevel }, { kind: 'source' as const, title: 'Nach Quelle', rows: data.bySource }]"
        :key="table.kind"
        :aria-labelledby="`${table.kind}-heading`"
        class="stats__section"
      >
        <h2 :id="`${table.kind}-heading`">
          {{ table.title }}
        </h2>
        <div class="stats__scroll">
          <table class="stats__table">
            <caption class="visually-hidden">
              {{ table.title }}: Anzahl und Anteil je gefordertem Deutsch
            </caption>
            <thead>
              <tr>
                <th scope="col">
                  {{ table.kind === 'remote' ? 'Arbeitsort' : 'Quelle' }}
                </th>
                <th v-for="level in data.overall" :key="level.level" scope="col" class="stats__number">
                  {{ level.label }}
                </th>
                <th scope="col" class="stats__number">
                  Gesamt
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in table.rows" :key="row.key">
                <th scope="row">
                  {{ breakdownLabel(table.kind, row) }}
                </th>
                <td v-for="level in data.overall" :key="level.level" class="stats__number">
                  <template v-if="cell(row.levels, level.level)?.count">
                    <NuxtLink :to="listLink(level.level, table.kind, row.key)">{{ cell(row.levels, level.level)!.count }}</NuxtLink>
                    <span class="stats__muted">({{ percent(cell(row.levels, level.level)!.percent) }})</span>
                  </template>
                  <template v-else>
                    0
                  </template>
                </td>
                <td class="stats__number">
                  {{ row.total }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </template>
    <ExtractionPanel />
    <DailyRunPanel />
    <FilterPanel />
    <ChannelPanel />
  </main>
</template>

<style scoped lang="scss">
.stats {
  display: grid;
  gap: var(--space-4);
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-4);

  &__header {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-3);

    h1 {
      margin: 0;
    }
  }

  &__hint,
  &__muted {
    color: var(--color-muted);
  }

  &__hint {
    margin: 0;
  }

  &__muted {
    margin-left: var(--space-1);
    font-size: 0.8rem;
  }

  &__section {
    display: grid;
    gap: var(--space-2);
    padding: var(--space-4);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
    background: var(--color-surface);

    h2 {
      margin: 0;
      font-size: 1.1rem;
    }
  }

  &__scroll {
    overflow-x: auto;
  }

  &__table {
    border-collapse: collapse;
    font-size: 0.9rem;

    th,
    td {
      padding: var(--space-1) var(--space-3);
      border-bottom: 1px solid var(--color-border);
      text-align: left;
      white-space: nowrap;
    }
  }

  &__number {
    text-align: right !important;
    font-variant-numeric: tabular-nums;
  }
}
</style>
