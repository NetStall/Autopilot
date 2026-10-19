<script setup lang="ts">
import type { Application, TrackerImportReport } from '~/types/application'
import type { ApplicationFilters } from '~/utils/applications'

useHead({ title: 'Bewerbungen · Job Autopilot' })

const route = useRoute()
const router = useRouter()
const api = useVacanciesApi()
const toasts = useToasts()

const filters = computed(() => applicationFiltersFromQuery(route.query))
const { data, status, error, refresh } = useAsyncData(
  'applications',
  () => api.listApplications({
    status: filters.value.status || undefined,
    channel: filters.value.channel || undefined,
    followUpDue: filters.value.followUpDue || undefined,
  }),
  { watch: [filters], server: false, lazy: true },
)
// All applications (unfiltered) for the channel select and the counters.
const { data: all, refresh: refreshAll } = useAsyncData('applications-all', () => api.listApplications({}), { server: false, lazy: true })

const rows = computed(() => sortForTable(data.value ?? []))
const channels = computed(() => channelsOf(all.value ?? []))
const dueCount = computed(() => (all.value ?? []).filter(a => a.isFollowUpDue).length)
const openId = ref<string | null>(null)
const importing = ref(false)
const report = ref<TrackerImportReport | null>(null)

function update(patch: Partial<ApplicationFilters>) {
  void router.push({ query: applicationFiltersToQuery({ ...filters.value, ...patch }) })
}

function onStatus(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  update({ status: APPLICATION_STATUSES.some(s => s.value === value) ? value as ApplicationFilters['status'] : '' })
}

function onChannel(event: Event) {
  update({ channel: (event.target as HTMLSelectElement).value })
}

function onFollowUp(event: Event) {
  update({ followUpDue: (event.target as HTMLInputElement).checked })
}

async function importTracker() {
  importing.value = true
  try {
    report.value = await api.importTracker()
    toasts.show(`Tracker importiert: ${report.value.imported} neu, ${report.value.updated} aktualisiert.`)
    await Promise.all([refresh(), refreshAll()])
  }
  catch (e: unknown) {
    toasts.error(`Import fehlgeschlagen: ${describeApiError(e)}`)
  }
  finally {
    importing.value = false
  }
}

function changed(updated: Application) {
  for (const list of [data, all]) {
    if (list.value) {
      list.value = list.value.map(a => (a.id === updated.id ? { ...updated, events: null } : a))
    }
  }
}
</script>

<template>
  <main class="applications">
    <div class="applications__header">
      <h1>Bewerbungen</h1>
      <p class="applications__meta">
        <span v-if="all">{{ all.length }} insgesamt</span>
        <span v-if="dueCount > 0" class="status status--due">{{ dueCount }} Follow-up fällig</span>
        <button type="button" class="button button--secondary button--small" :disabled="importing" @click="importTracker">
          {{ importing ? 'Importiere …' : 'Tracker importieren' }}
        </button>
      </p>
    </div>

    <div v-if="report" class="applications__report" role="status">
      <p>
        Import: {{ report.total }} Einträge · {{ report.imported }} neu · {{ report.updated }} aktualisiert ·
        {{ report.unchanged }} unverändert · {{ report.linkedToVacancy }} mit Stelle verknüpft
      </p>
      <details v-if="report.warnings.length > 0">
        <summary>{{ report.warnings.length }} Hinweise</summary>
        <ul>
          <li v-for="(warning, index) in report.warnings" :key="index">
            {{ warning }}
          </li>
        </ul>
      </details>
    </div>

    <form class="applications__filters" aria-label="Bewerbungen filtern" @submit.prevent>
      <div>
        <label for="app-status">Status</label>
        <select id="app-status" class="input" :value="filters.status" @change="onStatus">
          <option value="">
            Alle
          </option>
          <option v-for="option in APPLICATION_STATUSES" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </div>
      <div>
        <label for="app-channel">Kanal</label>
        <select id="app-channel" class="input" :value="filters.channel" @change="onChannel">
          <option value="">
            Alle Kanäle
          </option>
          <option v-for="channel in channels" :key="channel" :value="channel">
            {{ channel }}
          </option>
        </select>
      </div>
      <label class="applications__check">
        <input type="checkbox" :checked="filters.followUpDue" @change="onFollowUp">
        Nur Follow-up fällig
      </label>
    </form>

    <div v-if="status === 'pending' && !data" class="state" role="status">
      Bewerbungen werden geladen …
    </div>
    <div v-else-if="error" class="state state--error" role="alert">
      {{ describeApiError(error) }}
    </div>
    <div v-else-if="rows.length === 0" class="state">
      <p><strong>Keine Bewerbungen.</strong></p>
      <p>Markiere eine Stelle mit „Als beworben markieren" oder importiere deinen Tracker (data/import/bewerbungen-export.json).</p>
    </div>

    <div v-else class="applications__table-wrap">
      <table class="applications__table">
        <caption class="visually-hidden">
          Bewerbungen
        </caption>
        <thead>
          <tr>
            <th scope="col">
              Firma / Position
            </th>
            <th scope="col">
              Status
            </th>
            <th scope="col">
              Kanal
            </th>
            <th scope="col">
              Beworben
            </th>
            <th scope="col">
              Follow-up
            </th>
            <th scope="col">
              <span class="visually-hidden">Aktion</span>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in rows" :key="row.id" :class="{ 'applications__row--due': row.isFollowUpDue }">
            <th scope="row">
              <span class="applications__company">{{ row.company }}</span>
              <span class="applications__position">{{ row.position }}</span>
            </th>
            <td>
              <span class="status" :class="`status--${statusTone(row.status)}`">{{ row.statusLabel }}</span>
            </td>
            <td>{{ row.channel ?? '–' }}</td>
            <td><time :datetime="row.appliedAt">{{ formatDate(row.appliedAt) }}</time></td>
            <td>
              <template v-if="row.followUpAt">
                <time :datetime="row.followUpAt">{{ formatDate(row.followUpAt) }}</time>
                <span v-if="row.isFollowUpDue" class="status status--due">fällig</span>
              </template>
              <template v-else>
                –
              </template>
            </td>
            <td>
              <button type="button" class="button button--secondary button--small" @click="openId = row.id">
                Details<span class="visually-hidden"> zu {{ row.company }}</span>
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <ApplicationDrawer v-if="openId" :application-id="openId" @close="openId = null" @changed="changed" />
  </main>
</template>

<style scoped lang="scss">
.applications {
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);

  &__header {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-3);

    h1 {
      margin: 0;
      font-size: 1.6rem;
    }
  }

  &__meta {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-3);
    margin: 0;
    color: var(--color-muted);
  }

  &__report {
    margin-top: var(--space-3);
    padding: var(--space-3);
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);

    p {
      margin: 0;
    }
  }

  &__filters {
    display: flex;
    flex-wrap: wrap;
    align-items: end;
    gap: var(--space-3) var(--space-4);
    margin: var(--space-4) 0;
    padding: var(--space-3) var(--space-4);
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);

    label {
      display: block;
      font-size: 0.85rem;
      font-weight: 600;
    }
  }

  &__check {
    display: inline-flex !important;
    align-items: center;
    gap: var(--space-2);
    font-weight: 400 !important;
  }

  &__table-wrap {
    overflow-x: auto;
  }

  &__table {
    width: 100%;
    border-collapse: collapse;
    background: var(--color-surface);

    th,
    td {
      padding: var(--space-2) var(--space-3);
      border-bottom: 1px solid var(--color-border);
      text-align: left;
      vertical-align: top;
    }

    thead th {
      font-size: 0.85rem;
      color: var(--color-muted);
    }
  }

  &__row--due {
    background: var(--color-warning-bg);
  }

  &__company {
    display: block;
    font-weight: 600;
  }

  &__position {
    display: block;
    font-weight: 400;
    font-size: 0.9rem;
    color: var(--color-muted);
  }
}
</style>
