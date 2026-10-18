<script setup lang="ts">
import type { LocationQueryRaw } from 'vue-router'
import type { Vacancy } from '~/types/vacancy'

defineProps<{
  vacancies: Vacancy[]
  listQuery: LocationQueryRaw
}>()

const emit = defineEmits<{
  'toggle-seen': [vacancy: Vacancy]
  'opened-original': [vacancy: Vacancy]
}>()
</script>

<template>
  <ul class="vacancy-list">
    <li v-for="vacancy in vacancies" :key="vacancy.id">
      <VacancyCard
        :vacancy="vacancy"
        :list-query="listQuery"
        @toggle-seen="emit('toggle-seen', $event)"
        @opened-original="emit('opened-original', $event)"
      />
    </li>
  </ul>
</template>

<style scoped lang="scss">
.vacancy-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: var(--space-3);
}
</style>
