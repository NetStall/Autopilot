<script setup lang="ts">
import type { LocationQueryRaw } from 'vue-router'
import type { Vacancy } from '~/types/vacancy'

withDefaults(defineProps<{
  vacancies: Vacancy[]
  listQuery: LocationQueryRaw
  highlightKeywords?: readonly string[]
  activeId?: string | null
}>(), { highlightKeywords: () => [], activeId: null })

const emit = defineEmits<{
  'toggle-seen': [vacancy: Vacancy]
  'toggle-star': [vacancy: Vacancy]
  'toggle-dismiss': [vacancy: Vacancy]
  'opened-original': [vacancy: Vacancy]
  'activate': [vacancy: Vacancy]
}>()
</script>

<template>
  <ul class="vacancy-list">
    <li v-for="vacancy in vacancies" :key="vacancy.id">
      <VacancyCard
        :vacancy="vacancy"
        :list-query="listQuery"
        :highlight-keywords="highlightKeywords"
        :active="vacancy.id === activeId"
        @toggle-seen="emit('toggle-seen', $event)"
        @toggle-star="emit('toggle-star', $event)"
        @toggle-dismiss="emit('toggle-dismiss', $event)"
        @opened-original="emit('opened-original', $event)"
        @activate="emit('activate', $event)"
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
