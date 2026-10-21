<script setup lang="ts">
import type { FailedExtraction } from '~/types/extraction'

/**
 * „KI-Analyse": status of the background extraction, „Alle analysieren" for the current Top N (remembered in this
 * browser) with the expected duration, cancel, and the list behind „ungültig" with „Erneut".
 */
const TOP_CHOICES = [50, 100, 200, 500] as const
const STORAGE_KEY = 'extraction-top-n'

const api = useVacanciesApi()
const { data: status, refresh } = useAsyncData('extraction-status', () => api.extractionStatus(), { server: false, lazy: true })
const top = ref<number>(100)
const pending = ref<{ pending: number, estimatedSeconds: number } | null>(null)
const failed = ref<FailedExtraction[] | null>(null)
const error = ref<string | null>(null)
let timer: ReturnType<typeof setInterval> | undefined

const running = computed(() => status.value?.state === 'Running' || status.value?.state === 'Queued')
const estimate = computed(() => {
  const p = pending.value
  if (!p) {
    return null
  }
  if (p.pending === 0) {
    return 'alle schon analysiert'
  }
  const minutes = Math.max(1, Math.round(p.estimatedSeconds / 60))
  return `${p.pending} offen · ≈ ${minutes} min`
})

onMounted(() => {
  try {
    const stored = Number(window.localStorage.getItem(STORAGE_KEY))
    if ((TOP_CHOICES as readonly number[]).includes(stored)) {
      top.value = stored
    }
  }
  catch {
    // No storage (private window): the default stays.
  }
})

watch(top, (n) => {
  try {
    window.localStorage.setItem(STORAGE_KEY, String(n))
  }
  catch {
    // Remembering is a convenience only.
  }
})

watch([top, () => status.value?.available, running], async () => {
  if (!status.value?.available || running.value) {
    return
  }
  try {
    pending.value = await api.pendingExtractions(top.value)
  }
  catch {
    pending.value = null
  }
}, { immediate: true })

watch(running, (busy) => {
  clearInterval(timer)
  if (busy) {
    timer = setInterval(() => void refresh(), 2000)
  }
}, { immediate: true })
onBeforeUnmount(() => clearInterval(timer))

async function act(action: () => Promise<unknown>) {
  error.value = null
  try {
    await action()
    await refresh()
  }
  catch (e: unknown) {
    error.value = describeApiError(e)
  }
}

async function toggleFailed() {
  if (failed.value) {
    failed.value = null
    return
  }
  await act(async () => (failed.value = await api.failedExtractions()))
}

async function retry(item: FailedExtraction) {
  await act(() => api.extractVacancy(item.vacancyId, true))
  failed.value = failed.value?.filter(f => f.vacancyId !== item.vacancyId) ?? null
}
</script>

<template>
  <section class="extraction" aria-labelledby="extraction-heading">
    <h2 id="extraction-heading">
      KI-Analyse der Anforderungen
    </h2>
    <p v-if="status && !status.available" class="extraction__note">
      Nicht eingerichtet – es gelten die Regeln. Ollama installieren, Modell pullen und
      <code>Llm:Providers:Ollama:Enabled</code> einschalten (README „Local model").
    </p>
    <template v-else-if="status">
      <p>
        Modell <strong>{{ status.model }}</strong> ·
        <span v-if="running">läuft: {{ status.done }} / {{ status.total }}</span>
        <span v-else>bereit</span>
        <span v-if="status.extracted || status.failed">
          · {{ status.extracted }} analysiert, {{ status.cached }} aus dem Cache,
          <button type="button" class="extraction__link" :aria-expanded="!!failed" @click="toggleFailed">{{ status.failed }} ungültig</button>
        </span>
      </p>
      <p v-if="status.secondsPerVacancy" class="extraction__note">
        {{ status.secondsPerVacancy }} s pro Stelle<span v-if="status.tokensPerSecond"> · {{ status.tokensPerSecond }} Tokens/s</span>
      </p>
      <p v-if="status.lastError" class="extraction__error" role="status">
        {{ status.lastError }}
      </p>
      <div class="extraction__actions">
        <label for="extraction-top">Top
          <select id="extraction-top" v-model.number="top" class="input" :disabled="running">
            <option v-for="n in TOP_CHOICES" :key="n" :value="n">{{ n }}</option>
          </select>
        </label>
        <button type="button" class="button" :disabled="running || pending?.pending === 0" @click="act(() => api.startExtraction(top))">
          Alle analysieren
        </button>
        <span v-if="estimate && !running" class="extraction__note" data-test="estimate">{{ estimate }}</span>
        <button v-if="running" type="button" class="button button--secondary" @click="act(api.cancelExtraction)">
          Abbrechen
        </button>
      </div>
      <ul v-if="failed" class="extraction__failed" aria-label="Ungültige Analysen">
        <li v-if="failed.length === 0">
          Keine – alles gültig.
        </li>
        <li v-for="item in failed" :key="item.vacancyId">
          <NuxtLink :to="`/vacancies/${item.vacancyId}`">{{ item.title }}</NuxtLink> · {{ item.company }}
          <span class="extraction__note">{{ item.error }}</span>
          <button type="button" class="button button--small" @click="retry(item)">
            Erneut
          </button>
        </li>
      </ul>
    </template>
    <p v-if="error" class="extraction__error" role="alert">
      {{ error }}
    </p>
  </section>
</template>

<style scoped lang="scss">
.extraction {
  margin-top: 2rem;
  display: grid;
  gap: 0.5rem;

  &__note {
    color: var(--color-muted);
  }

  &__error {
    color: var(--color-error-text);
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.5rem;
  }

  &__link {
    background: none;
    border: none;
    padding: 0;
    color: var(--color-accent);
    text-decoration: underline;
    cursor: pointer;
    font: inherit;
  }

  &__failed {
    display: grid;
    gap: 0.5rem;
    padding-left: 1.25rem;
  }
}
</style>
