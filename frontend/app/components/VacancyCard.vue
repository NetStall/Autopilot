<script setup lang="ts">
import type { LocationQueryRaw } from 'vue-router'
import type { Vacancy } from '~/types/vacancy'

const props = defineProps<{
  vacancy: Vacancy
  /** Current list filters, carried to the detail page so its back link restores them. */
  listQuery: LocationQueryRaw
}>()

const emit = defineEmits<{
  'toggle-seen': [vacancy: Vacancy]
  'opened-original': [vacancy: Vacancy]
}>()

const originalUrl = computed(() => safeExternalUrl(props.vacancy.url))
const dateLabel = computed(() => props.vacancy.publishedAt
  ? `Veröffentlicht ${formatRelativeDate(props.vacancy.publishedAt)}`
  : `Importiert ${formatRelativeDate(props.vacancy.fetchedAt)}`)
const dateTitle = computed(() => formatDate(props.vacancy.publishedAt ?? props.vacancy.fetchedAt))
</script>

<template>
  <article class="card" :class="{ 'card--seen': vacancy.isSeen }" :aria-labelledby="`vacancy-${vacancy.id}`">
    <div class="card__main">
      <div class="card__heading">
        <span v-if="!vacancy.isSeen" class="badge badge--new">Neu</span>
        <h2 :id="`vacancy-${vacancy.id}`" class="card__title">
          <NuxtLink :to="{ path: `/vacancies/${vacancy.id}`, query: listQuery }">
            {{ vacancy.title }}
          </NuxtLink>
        </h2>
      </div>
      <p class="card__meta">
        <span>{{ vacancy.company }}</span>
        <span v-if="vacancy.location"> · {{ vacancy.location }}</span>
      </p>
      <p class="card__date">
        <time :datetime="vacancy.publishedAt ?? vacancy.fetchedAt" :title="dateTitle">{{ dateLabel }}</time>
      </p>
    </div>

    <div class="card__side">
      <EmployerTypeBadge :type="vacancy.employerType" />
      <div class="card__actions">
        <a
          v-if="originalUrl"
          class="button button--small"
          :href="originalUrl"
          target="_blank"
          rel="noopener noreferrer"
          @click="emit('opened-original', vacancy)"
          @auxclick="emit('opened-original', vacancy)"
        >
          Öffnen<span class="visually-hidden"> (Originalanzeige, neuer Tab)</span>
        </a>
        <button type="button" class="button button--secondary button--small" @click="emit('toggle-seen', vacancy)">
          {{ vacancy.isSeen ? 'Als neu markieren' : 'Als gesehen markieren' }}
        </button>
      </div>
    </div>
  </article>
</template>

<style scoped lang="scss">
.card {
  display: flex;
  justify-content: space-between;
  gap: var(--space-4);
  padding: var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-left: 4px solid var(--color-accent);
  border-radius: var(--radius);

  &--seen {
    border-left-color: var(--color-border);
  }

  &__main {
    min-width: 0;
  }

  &__heading {
    display: flex;
    align-items: baseline;
    gap: var(--space-2);
  }

  &__title {
    margin: 0;
    font-size: 1.05rem;
    font-weight: 600;
    overflow-wrap: anywhere;

    a {
      color: inherit;
    }
  }

  &__meta {
    margin: var(--space-1) 0 0;
    color: var(--color-muted);
    overflow-wrap: anywhere;
  }

  &__date {
    margin: var(--space-1) 0 0;
    font-size: 0.85rem;
    color: var(--color-muted);
  }

  &__side {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: var(--space-3);
    flex-shrink: 0;
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: var(--space-2);
  }
}

@media (max-width: 640px) {
  .card {
    flex-direction: column;

    &__side {
      align-items: flex-start;
    }

    &__actions {
      justify-content: flex-start;
    }
  }
}
</style>
