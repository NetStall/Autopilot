<script setup lang="ts">
const api = useVacanciesApi()
const importVersion = useImportVersion()

const running = ref(false)
const summary = ref<string | null>(null)
const errorMessage = ref<string | null>(null)

async function startImport() {
  running.value = true
  summary.value = null
  errorMessage.value = null
  try {
    const result = await api.runImport()
    summary.value = formatImportSummary(result)
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
