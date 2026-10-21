<script setup lang="ts">
/**
 * „Stelle manuell hinzufügen" (step 07b): a posting from LinkedIn or a company site. The text is pasted, never fetched;
 * the vacancy gets source „Manuell" and is scored, analysed and labelable like any other.
 */
const api = useVacanciesApi()
const form = reactive({ title: '', company: '', location: '', url: '', description: '' })
const saving = ref(false)
const error = ref<string | null>(null)

const valid = computed(() => form.title.trim() !== '' && form.company.trim() !== '')

async function save() {
  if (!valid.value) {
    error.value = 'Titel und Firma sind Pflicht.'
    return
  }
  saving.value = true
  error.value = null
  try {
    const created = await api.createVacancy({
      title: form.title.trim(),
      company: form.company.trim(),
      location: form.location.trim() || null,
      url: form.url.trim() || null,
      description: form.description.trim() || null,
    })
    await navigateTo(`/vacancies/${created.id}`)
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
  <main class="page manual">
    <NuxtLink to="/" class="manual__back">
      ← Zur Liste
    </NuxtLink>
    <h1>Stelle manuell hinzufügen</h1>
    <p class="manual__help">
      Für Stellen von LinkedIn oder Firmenseiten: Text aus der Anzeige einfügen. Der Link wird nicht abgerufen.
    </p>
    <form class="manual__form" @submit.prevent="save">
      <label for="manual-title">Titel *
        <input id="manual-title" v-model="form.title" class="input" type="text" required maxlength="300">
      </label>
      <label for="manual-company">Firma *
        <input id="manual-company" v-model="form.company" class="input" type="text" required maxlength="300">
      </label>
      <label for="manual-location">Ort
        <input id="manual-location" v-model="form.location" class="input" type="text" maxlength="300" placeholder="z. B. 94469 Deggendorf oder Remote (Deutschland)">
      </label>
      <label for="manual-url">Link zur Anzeige
        <input id="manual-url" v-model="form.url" class="input" type="url" maxlength="2000" placeholder="https://…">
      </label>
      <label for="manual-description">Text der Anzeige
        <textarea id="manual-description" v-model="form.description" class="input manual__text" rows="14" />
      </label>
      <p v-if="error" class="manual__error" role="alert">
        {{ error }}
      </p>
      <button type="submit" class="button" :disabled="saving || !valid">
        {{ saving ? 'Speichert …' : 'Speichern und bewerten' }}
      </button>
    </form>
  </main>
</template>

<style scoped lang="scss">
.manual {
  max-width: 48rem;

  &__help {
    color: var(--color-muted);
  }

  &__form {
    display: grid;
    gap: 0.75rem;

    label {
      display: grid;
      gap: 0.25rem;
    }
  }

  &__text {
    font: inherit;
  }

  &__error {
    color: var(--color-error-text);
  }
}
</style>
