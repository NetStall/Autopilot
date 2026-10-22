<script setup lang="ts">
import type { ApplicationPackage, FollowUp, PackageEstimate, PackageResult, ReadyPackage } from '~/types/package'

useHead({ title: 'Heute bereit · Job Autopilot' })

/**
 * „Heute bereit" (08c): prepared packages, oldest first — open, review („Unterlagen prüfen" → Freigeben), mark as sent
 * after I sent it myself, or discard; „Top K vorbereiten" with the cost shown before; the follow-ups that are due.
 * The tool never sends anything.
 */
const api = useVacanciesApi()
const toasts = useToasts()
const route = useRoute()

const packages = ref<ReadyPackage[]>([])
const followUps = ref<FollowUp[]>([])
const open = ref<Record<string, ApplicationPackage>>({})
const k = ref(5)
const estimate = ref<PackageEstimate | null>(null)
const preparing = ref(false)
const results = ref<PackageResult[]>([])
const busy = ref<string | null>(null)
const error = ref<string | null>(null)

async function load() {
  error.value = null
  try {
    const [ready, due] = await Promise.all([api.readyPackages(), api.followUps()])
    packages.value = ready
    followUps.value = due
    estimate.value = await api.packageEstimate(k.value)
  }
  catch (e: unknown) {
    error.value = `Nicht geladen: ${describeApiError(e)}`
  }
}

async function toggle(applicationId: string) {
  if (open.value[applicationId]) {
    const { [applicationId]: _, ...rest } = open.value
    open.value = rest
    return
  }
  try {
    open.value = { ...open.value, [applicationId]: await api.getPackage(applicationId) }
  }
  catch (e: unknown) {
    toasts.error(`Paket nicht geladen: ${describeApiError(e)}`)
  }
}

async function prepareTop() {
  preparing.value = true
  try {
    results.value = await api.prepareTopPackages(k.value)
    await load()
  }
  catch (e: unknown) {
    toasts.error(`Vorbereitung fehlgeschlagen: ${describeApiError(e)}`)
  }
  finally {
    preparing.value = false
  }
}

async function markSent(item: ReadyPackage) {
  busy.value = item.applicationId
  try {
    await api.markSent(item.applicationId, sentChannel(item.channel))
    toasts.show(`${item.company}: als gesendet markiert.`)
    await load()
  }
  catch (e: unknown) {
    toasts.error(`Nicht markiert: ${describeApiError(e)}`)
  }
  finally {
    busy.value = null
  }
}

async function discard(item: ReadyPackage) {
  busy.value = item.applicationId
  try {
    await api.changeApplicationStatus(item.applicationId, 'Withdrawn', 'Bewerbungspaket verworfen')
    await load()
  }
  catch (e: unknown) {
    toasts.error(`Nicht verworfen: ${describeApiError(e)}`)
  }
  finally {
    busy.value = null
  }
}

async function followUpDone(item: FollowUp) {
  busy.value = item.applicationId
  try {
    await api.followUpDone(item.applicationId)
    await load()
  }
  finally {
    busy.value = null
  }
}

watch(k, async () => {
  estimate.value = await api.packageEstimate(k.value)
})

onMounted(async () => {
  await load()
  const requested = typeof route.query.open === 'string' ? route.query.open : null
  if (requested && packages.value.some(p => p.applicationId === requested)) {
    await toggle(requested)
  }
})
</script>

<template>
  <main class="page">
    <h1>Heute bereit</h1>
    <p class="page__help">
      Vorbereitete Bewerbungen – prüfen, freigeben, selbst senden, dann „Als gesendet markieren“. Das Tool sendet nichts.
    </p>
    <p v-if="error" role="alert" class="page__error">
      {{ error }}
    </p>

    <section class="prepare" aria-labelledby="prepare-heading">
      <h2 id="prepare-heading">
        Top K vorbereiten
      </h2>
      <label for="prepare-k">Anzahl</label>
      <select id="prepare-k" v-model.number="k" class="input">
        <option v-for="n in [1, 3, 5, 10]" :key="n" :value="n">
          {{ n }}
        </option>
      </select>
      <p v-if="estimate">
        {{ estimate.candidates.length }} Stellen ohne Bewerbung · {{ estimate.calls }} Claude-Aufrufe · ≈ {{ estimate.costUsd.toFixed(2) }} $ ·
        heute noch {{ estimate.remainingCalls }} Aufrufe frei
      </p>
      <ul v-if="estimate?.candidates.length" class="prepare__candidates">
        <li v-for="c in estimate.candidates" :key="c.vacancyId">
          {{ c.title }} · {{ c.company }} ({{ c.rankScore ?? '–' }})
        </li>
      </ul>
      <button
        type="button"
        class="button"
        :disabled="preparing || !estimate?.candidates.length || !estimate.withinLimit"
        :aria-busy="preparing"
        @click="prepareTop"
      >
        {{ preparing ? 'Wird vorbereitet (bis zu 1 Minute je Stelle) …' : `Top ${k} vorbereiten` }}
      </button>
      <p v-if="estimate && !estimate.withinLimit" class="page__error">
        Das Tageslimit reicht dafür nicht (Claude:DailyCallLimit).
      </p>
      <ul v-if="results.length">
        <li v-for="r in results" :key="r.vacancyId" :class="{ page__error: r.error }">
          {{ r.title }}: {{ r.error ?? 'vorbereitet' }}
        </li>
      </ul>
    </section>

    <section aria-labelledby="ready-heading">
      <h2 id="ready-heading">
        Pakete ({{ packages.length }})
      </h2>
      <p v-if="!packages.length">
        Noch keine Bewerbungspakete.
      </p>
      <article v-for="item in packages" :key="item.applicationId" class="ready">
        <div class="ready__head">
          <div>
            <strong>{{ item.position }}</strong> · {{ item.company }}
            <span class="badge">{{ CHANNEL_LABELS[item.channel] }}</span>
            <span v-if="item.approved" class="badge ready__approved">Freigegeben</span>
            <span v-if="item.warnings" class="badge ready__warn">{{ item.warnings }} Hinweise</span>
            <span class="ready__date">vorbereitet {{ formatRelativeDate(item.generatedAt) }}</span>
          </div>
          <div class="ready__actions">
            <button type="button" class="button button--secondary button--small" :aria-expanded="!!open[item.applicationId]" @click="toggle(item.applicationId)">
              {{ open[item.applicationId] ? 'Schließen' : 'Öffnen' }}
            </button>
            <NuxtLink :to="`/unterlagen/${item.applicationId}`" class="button button--secondary button--small">
              Unterlagen prüfen
            </NuxtLink>
            <button
              type="button"
              class="button button--small"
              :disabled="!item.approved || busy === item.applicationId"
              :title="item.approved ? undefined : 'Erst freigeben, dann selbst senden'"
              @click="markSent(item)"
            >
              Als gesendet markieren
            </button>
            <button type="button" class="button button--secondary button--small" :disabled="busy === item.applicationId" @click="discard(item)">
              Verwerfen
            </button>
          </div>
        </div>
        <PackageView v-if="open[item.applicationId]" :pkg="open[item.applicationId]!" />
      </article>
    </section>

    <section aria-labelledby="followups-heading">
      <h2 id="followups-heading">
        Follow-ups fällig ({{ followUps.length }})
      </h2>
      <p v-if="!followUps.length">
        Nichts fällig.
      </p>
      <article v-for="f in followUps" :key="f.applicationId" class="ready">
        <div class="ready__head">
          <div>
            <strong>{{ f.position }}</strong> · {{ f.company }} · {{ f.statusLabel }} · beworben am {{ formatDate(f.appliedAt) }}
          </div>
          <button type="button" class="button button--small" :disabled="busy === f.applicationId" @click="followUpDone(f)">
            Erledigt
          </button>
        </div>
        <p><strong>Betreff:</strong> {{ f.draft.subject }}</p>
        <pre class="ready__text">{{ f.draft.email }}</pre>
        <p><strong>LinkedIn:</strong> {{ f.draft.linkedIn }}</p>
      </article>
    </section>
  </main>
</template>

<style scoped lang="scss">
.page {
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);
  display: grid;
  gap: var(--space-5);

  &__help {
    color: var(--color-muted);
  }

  &__error {
    color: var(--color-error-text);
  }
}

.ready {
  padding: var(--space-3) var(--space-4);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-surface);
  margin-bottom: var(--space-3);

  &__head {
    display: flex;
    flex-wrap: wrap;
    justify-content: space-between;
    gap: var(--space-2);
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
  }

  &__date {
    color: var(--color-muted);
    font-size: 0.85rem;
    margin-left: var(--space-2);
  }

  &__approved {
    color: var(--color-new-text);
  }

  &__warn {
    color: var(--color-warning-text);
  }

  &__text {
    white-space: pre-wrap;
    font-family: inherit;
  }
}
</style>
