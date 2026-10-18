<script setup lang="ts">
import type { VacancyFilters } from '~/utils/vacancyFilters'

const props = defineProps<{
  filters: VacancyFilters
  pageSize: number
  totalCount: number
}>()

const pageCount = computed(() => Math.max(1, Math.ceil(props.totalCount / props.pageSize)))
const linkTo = (page: number) => ({ query: filtersToQuery({ ...props.filters, page }) })
</script>

<template>
  <nav v-if="pageCount > 1" class="pagination" aria-label="Seiten">
    <NuxtLink
      v-if="filters.page > 1"
      class="button button--secondary button--small"
      :to="linkTo(filters.page - 1)"
      rel="prev"
    >
      ← Zurück
    </NuxtLink>
    <span class="pagination__current" aria-current="page">Seite {{ filters.page }} von {{ pageCount }}</span>
    <NuxtLink
      v-if="filters.page < pageCount"
      class="button button--secondary button--small"
      :to="linkTo(filters.page + 1)"
      rel="next"
    >
      Weiter →
    </NuxtLink>
  </nav>
</template>

<style scoped lang="scss">
.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: var(--space-3);
  margin-top: var(--space-5);

  &__current {
    color: var(--color-muted);
  }
}
</style>
