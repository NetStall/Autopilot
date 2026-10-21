<script setup lang="ts">
import type { Application, ApplicationStatus } from '~/types/application'

/**
 * „Status" of the application right on the vacancy page (08a): Absage, Interview … without going to „Bewerbungen".
 * Offers the same statuses as the applications page (a draft can only be withdrawn).
 */
const props = defineProps<{ applicationId: string, status: ApplicationStatus }>()
const emit = defineEmits<{ changed: [application: Application] }>()

const api = useVacanciesApi()
const saving = ref(false)
const error = ref<string | null>(null)
const options = computed(() => manualStatusOptions(props.status))

async function change(event: Event) {
  const next = (event.target as HTMLSelectElement).value as ApplicationStatus
  if (next === props.status) {
    return
  }
  saving.value = true
  error.value = null
  try {
    emit('changed', await api.changeApplicationStatus(props.applicationId, next))
  }
  catch (e: unknown) {
    error.value = `Status nicht gespeichert: ${describeApiError(e)}`
    ;(event.target as HTMLSelectElement).value = props.status
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <span class="app-status">
    <label :for="`status-${applicationId}`">Status</label>
    <select :id="`status-${applicationId}`" class="input" :value="status" :disabled="saving" @change="change">
      <option v-for="option in options" :key="option.value" :value="option.value">
        {{ option.label }}
      </option>
    </select>
    <span v-if="error" class="app-status__error" role="alert">{{ error }}</span>
  </span>
</template>

<style scoped lang="scss">
.app-status {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);

  &__error {
    color: var(--color-error-text);
  }
}
</style>
