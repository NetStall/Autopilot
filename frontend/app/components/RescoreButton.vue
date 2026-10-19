<script setup lang="ts">
import type { RescoreStatus } from '~/types/vacancy'

const POLL_INTERVAL_MS = 1000

const emit = defineEmits<{ finished: [status: RescoreStatus] }>()

const api = useVacanciesApi()
const toasts = useToasts()
const running = ref(false)
let pollTimer: ReturnType<typeof setTimeout> | undefined

async function poll() {
  clearTimeout(pollTimer)
  let status: RescoreStatus
  try {
    status = await api.rescoreStatus()
  }
  catch (error: unknown) {
    running.value = false
    toasts.error(`Status der Neubewertung nicht abrufbar: ${describeApiError(error)}`)
    return
  }
  if (status.state !== 'Idle') {
    pollTimer = setTimeout(() => void poll(), POLL_INTERVAL_MS)
    return
  }
  running.value = false
  if (status.lastError) {
    toasts.error(status.lastError)
  }
  else {
    toasts.show(`${status.lastScored ?? 0} Stellen neu bewertet.`)
  }
  emit('finished', status)
}

async function start() {
  running.value = true
  try {
    await api.startRescore()
    await poll()
  }
  catch (error: unknown) {
    running.value = false
    toasts.error(`Neubewertung fehlgeschlagen: ${describeApiError(error)}`)
  }
}

onBeforeUnmount(() => clearTimeout(pollTimer))
</script>

<template>
  <button
    type="button"
    class="button button--secondary button--small"
    :disabled="running"
    :aria-busy="running"
    title="Alle Stellen mit den aktuellen Regeln (Relevance, Remote) neu bewerten"
    @click="start"
  >
    {{ running ? 'Wird bewertet …' : 'Neu bewerten' }}
  </button>
</template>
