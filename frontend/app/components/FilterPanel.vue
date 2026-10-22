<script setup lang="ts">
/** „Harte Filter" (08b): how many vacancies each filter hides, and the model time that saves per full run. */
const api = useVacanciesApi()
const { data: stats, error } = useAsyncData('filter-statistics', () => api.filterStatistics(), { server: false, lazy: true })
const minutes = computed(() => (stats.value ? Math.round(stats.value.savedSeconds / 60) : 0))
</script>

<template>
  <section class="filters-panel" aria-labelledby="filters-heading">
    <h2 id="filters-heading">
      Harte Filter
    </h2>
    <p v-if="error" role="alert">
      Filter-Statistik nicht geladen: {{ describeApiError(error) }}
    </p>
    <template v-else-if="stats">
      <p>
        <strong>{{ stats.filtered }} von {{ stats.vacancies }} Stellen gefiltert</strong>
        – spart bei einem vollen KI-Lauf etwa {{ minutes }} min ({{ stats.secondsPerVacancy }} s je Stelle).
        <template v-if="stats.exempt">
          {{ stats.exempt }} bleiben trotzdem sichtbar (Bewerbung oder „Passt“).
        </template>
      </p>
      <ul>
        <li v-for="row in stats.reasons" :key="row.reason">
          {{ row.label }}: {{ row.count }}
        </li>
      </ul>
    </template>
  </section>
</template>

<style scoped lang="scss">
.filters-panel {
  margin-top: var(--space-6);
}
</style>
