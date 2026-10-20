<script setup lang="ts">
import type { Vacancy } from '~/types/vacancy'

/** „Beschreibung einfügen": the text copied from the original posting, stored as manual and scored again. */
const props = defineProps<{ vacancyId: string }>()
const emit = defineEmits<{ saved: [vacancy: Vacancy] }>()

const api = useVacanciesApi()
const open = ref(false)
const text = ref('')
const saving = ref(false)
const error = ref<string | null>(null)

async function save() {
  if (text.value.trim() === '') {
    error.value = 'Bitte den Text der Originalanzeige einfügen.'
    return
  }
  saving.value = true
  error.value = null
  try {
    emit('saved', await api.setDescription(props.vacancyId, text.value))
    open.value = false
    text.value = ''
  }
  catch (e: unknown) {
    error.value = `Konnte nicht gespeichert werden: ${describeApiError(e)}`
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="paste">
    <button v-if="!open" type="button" class="button" @click="open = true">
      Beschreibung einfügen
    </button>
    <form v-else class="paste__form" @submit.prevent="save">
      <label for="paste-text">Text aus der Originalanzeige (wird als „von Hand eingefügt" gespeichert und neu bewertet)</label>
      <textarea id="paste-text" v-model="text" class="input paste__text" rows="12" />
      <p v-if="error" class="paste__error" role="alert">
        {{ error }}
      </p>
      <div class="paste__actions">
        <button type="submit" class="button" :disabled="saving">
          {{ saving ? 'Speichert …' : 'Speichern und neu bewerten' }}
        </button>
        <button type="button" class="button" :disabled="saving" @click="open = false">
          Abbrechen
        </button>
      </div>
    </form>
  </div>
</template>

<style scoped lang="scss">
.paste {
  margin-top: 0.75rem;

  &__form {
    display: grid;
    gap: 0.5rem;
  }

  &__text {
    width: 100%;
    font: inherit;
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
