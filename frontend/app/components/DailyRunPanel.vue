<script setup lang="ts">
import type { Coverage, DailyRunStage, DailyRunStatus } from '~/types/statistics'

/**
 * „Tageslauf" (08b): one button for import → filters → scoring → KI → channels → summary, with the stages as they
 * finish, cancel, the summary card and the search coverage of the last 7 days. Nothing is sent from here.
 */
const STAGE_LABELS: Record<DailyRunStage, string> = {
  Import: 'Import',
  Filters: 'Harte Filter',
  Scoring: 'Bewertung',
  Extraction: 'KI-Analyse',
  Channels: 'Bewerbungswege',
  Summary: 'Zusammenfassung',
}

const api = useVacanciesApi()
const status = ref<DailyRunStatus | null>(null)
const coverage = ref<Coverage | null>(null)
const error = ref<string | null>(null)
let timer: ReturnType<typeof setTimeout> | undefined

const running = computed(() => status.value != null && status.value.state !== 'Idle')
const summary = computed(() => status.value?.last ?? null)
const filtered = computed(() => Object.entries(summary.value?.newFiltered ?? {}).map(([reason, count]) => `${reason} ${count}`).join(', '))

async function refresh() {
  try {
    status.value = await api.dailyRun()
    if (running.value) {
      timer = setTimeout(refresh, 2000)
    }
    else {
      coverage.value = await api.coverage(7)
    }
  }
  catch (e: unknown) {
    error.value = `Tageslauf nicht geladen: ${describeApiError(e)}`
  }
}

async function start() {
  error.value = null
  try {
    status.value = await api.startDailyRun(100)
    timer = setTimeout(refresh, 1000)
  }
  catch (e: unknown) {
    error.value = `Tageslauf nicht gestartet: ${describeApiError(e)}`
  }
}

async function cancel() {
  await api.cancelDailyRun()
  await refresh()
}

onMounted(refresh)
onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <section class="daily" aria-labelledby="daily-heading">
    <div class="daily__head">
      <h2 id="daily-heading">
        Tageslauf
      </h2>
      <button type="button" class="button" :disabled="running" :aria-busy="running" @click="start">
        {{ running ? `Läuft: ${status?.currentStage ? STAGE_LABELS[status.currentStage] : 'wartet'} …` : 'Tageslauf starten' }}
      </button>
      <button v-if="running" type="button" class="button button--secondary" @click="cancel">
        Abbrechen
      </button>
    </div>
    <p class="daily__help">
      Import aller Suchen → Harte Filter → Bewertung → KI-Analyse neuer Stellen (Top 100) → Bewerbungswege. Mehrfach am
      Tag möglich; ohne Ollama wird nur die KI übersprungen. Es wird nichts gesendet.
    </p>
    <p v-if="error" role="alert" class="daily__error">
      {{ error }}
    </p>

    <ol v-if="status?.stages.length" class="daily__stages" aria-live="polite">
      <li v-for="s in status.stages" :key="s.stage" :class="`daily__stage--${s.outcome.toLowerCase()}`">
        <strong>{{ STAGE_LABELS[s.stage] }}</strong> ({{ (s.durationMs / 1000).toFixed(1) }} s): {{ s.message }}
      </li>
    </ol>

    <div v-if="summary && !running" class="daily__card">
      <h3>Ergebnis vom {{ formatDateTime(summary.finishedAt) }}</h3>
      <p>
        {{ summary.newVacancies }} neue Stellen<template v-if="filtered">
          · neu gefiltert: {{ filtered }}
        </template>
      </p>
      <p v-if="summary.newInTop20.length">
        Neu in den Top 20:
      </p>
      <ul>
        <li v-for="v in summary.newInTop20" :key="v.vacancyId">
          <NuxtLink :to="`/vacancies/${v.vacancyId}`">
            {{ v.title }}
          </NuxtLink> · {{ v.company }} ({{ v.rankScore ?? '–' }})
        </li>
      </ul>
      <p>Per E-Mail vorbereitbar in den Top 20: {{ summary.emailReadyInTop20.length }}</p>
      <p v-for="f in summary.failures" :key="f" class="daily__error">
        {{ f }}
      </p>
    </div>

    <div v-if="coverage && coverage.searches.length" class="daily__coverage">
      <h3>Suchen der letzten {{ coverage.days }} Tage</h3>
      <table>
        <thead>
          <tr>
            <th scope="col">
              Suche
            </th>
            <th scope="col">
              Stellen
            </th>
            <th scope="col">
              in den Top 50
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in coverage.searches.slice(0, 10)" :key="s.name">
            <th scope="row">
              {{ s.name }}
            </th>
            <td>{{ s.vacancies }}</td>
            <td>{{ s.inTop50 }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="coverage.bringNothing.length">
        Bringen nichts in die Top 50: {{ coverage.bringNothing.join(', ') }}
      </p>
    </div>
  </section>
</template>

<style scoped lang="scss">
.daily {
  margin-top: var(--space-6);

  &__head {
    display: flex;
    align-items: baseline;
    gap: var(--space-3);
  }

  &__help {
    color: var(--color-muted);
    font-size: 0.9rem;
  }

  &__stage--skipped {
    color: var(--color-warning-text);
  }

  &__stage--failed,
  &__stage--cancelled,
  &__error {
    color: var(--color-error-text);
  }

  &__coverage table {
    border-collapse: collapse;

    th,
    td {
      padding: var(--space-1) var(--space-3);
      text-align: left;
    }
  }
}
</style>
