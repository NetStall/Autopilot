<script setup lang="ts">
import type { Vacancy } from '~/types/vacancy'

defineProps<{
  vacancies: Vacancy[]
}>()
</script>

<template>
  <ul class="vacancy-list">
    <li v-for="vacancy in vacancies" :key="vacancy.id" class="vacancy">
      <div class="vacancy__main">
        <h2 class="vacancy__title">
          <a v-if="vacancy.url" :href="vacancy.url" target="_blank" rel="noopener noreferrer">
            {{ vacancy.title }}
          </a>
          <template v-else>
            {{ vacancy.title }}
          </template>
        </h2>
        <p class="vacancy__meta">
          <span>{{ vacancy.company }}</span>
          <span v-if="vacancy.location"> · {{ vacancy.location }}</span>
        </p>
      </div>
      <span
        class="vacancy__badge"
        :class="{ 'vacancy__badge--intermediary': isIntermediary(vacancy.employerType) }"
      >
        {{ employerTypeLabel(vacancy.employerType) }}
      </span>
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

.vacancy {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
  padding: var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);

  &__title {
    margin: 0 0 var(--space-1);
    font-size: 1.05rem;
    font-weight: 600;

    a {
      color: inherit;
    }
  }

  &__meta {
    margin: 0;
    color: var(--color-muted);
  }

  &__badge {
    flex-shrink: 0;
    padding: 0.2rem 0.6rem;
    border-radius: 999px;
    font-size: 0.8rem;
    background: var(--color-badge);
    color: var(--color-badge-text);

    &--intermediary {
      background: var(--color-warning-bg);
      color: var(--color-warning-text);
    }
  }
}

@media (max-width: 560px) {
  .vacancy {
    flex-direction: column;
  }
}
</style>
