<script setup lang="ts">
const { data, status, error, refresh } = useVacancies()

const vacancies = computed(() => data.value?.items ?? [])
const isLoading = computed(() => status.value === 'idle' || status.value === 'pending')
</script>

<template>
  <main class="page">
    <header class="page__header">
      <h1>Job Autopilot</h1>
      <p v-if="data" class="page__count">{{ data.totalCount }} Stellen</p>
    </header>

    <section aria-live="polite" :aria-busy="isLoading">
      <p v-if="isLoading" class="state">Stellen werden geladen …</p>

      <div v-else-if="error" class="state state--error" role="alert">
        <p>Die Stellen konnten nicht geladen werden. Läuft das Backend?</p>
        <button type="button" class="button" @click="refresh()">Erneut versuchen</button>
      </div>

      <p v-else-if="vacancies.length === 0" class="state">
        Noch keine Stellen vorhanden.
      </p>

      <VacancyList v-else :vacancies="vacancies" />
    </section>
  </main>
</template>

<style scoped lang="scss">
.page {
  max-width: 56rem;
  margin: 0 auto;
  padding: var(--space-6) var(--space-4);

  &__header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    margin-bottom: var(--space-5);

    h1 {
      margin: 0;
      font-size: 1.75rem;
    }
  }

  &__count {
    margin: 0;
    color: var(--color-muted);
  }
}

.state {
  padding: var(--space-5);
  text-align: center;
  color: var(--color-muted);
  background: var(--color-surface);
  border: 1px dashed var(--color-border);
  border-radius: var(--radius);

  &--error {
    color: var(--color-error-text);
    border-color: var(--color-error-text);

    p {
      margin: 0 0 var(--space-3);
    }
  }
}
</style>
