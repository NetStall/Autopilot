<script setup lang="ts">
/** „KI-Analyse": status of the background extraction, start („Alle analysieren") and cancel. */
const api = useVacanciesApi()
const { data: status, refresh } = useAsyncData('extraction-status', () => api.extractionStatus(), { server: false, lazy: true })
const error = ref<string | null>(null)
let timer: ReturnType<typeof setInterval> | undefined

const running = computed(() => status.value?.state === 'Running' || status.value?.state === 'Queued')

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
        <span v-if="status.extracted">· {{ status.extracted }} analysiert, {{ status.cached }} aus dem Cache, {{ status.failed }} ungültig</span>
      </p>
      <p v-if="status.secondsPerVacancy" class="extraction__note">
        {{ status.secondsPerVacancy }} s pro Stelle<span v-if="status.tokensPerSecond"> · {{ status.tokensPerSecond }} Tokens/s</span>
      </p>
      <p v-if="status.lastError" class="extraction__error" role="status">
        {{ status.lastError }}
      </p>
      <div class="extraction__actions">
        <button type="button" class="button" :disabled="running" @click="act(api.startExtraction)">
          Alle analysieren
        </button>
        <button v-if="running" type="button" class="button button--secondary" @click="act(api.cancelExtraction)">
          Abbrechen
        </button>
      </div>
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
    gap: 0.5rem;
  }
}
</style>
