<script setup lang="ts">
import type { LocationQueryRaw } from 'vue-router'
import type { Vacancy } from '~/types/vacancy'

const props = withDefaults(defineProps<{
  vacancy: Vacancy
  /** Current list filters, carried to the detail page so its back link restores them. */
  listQuery: LocationQueryRaw
  /** Ui:HighlightKeywords — visual cue in the title only. */
  highlightKeywords?: readonly string[]
  /** The card the keyboard shortcuts act on. */
  active?: boolean
}>(), { highlightKeywords: () => [], active: false })

const emit = defineEmits<{
  'toggle-seen': [vacancy: Vacancy]
  'toggle-star': [vacancy: Vacancy]
  'toggle-dismiss': [vacancy: Vacancy]
  'opened-original': [vacancy: Vacancy]
  'activate': [vacancy: Vacancy]
}>()

const originalUrl = computed(() => safeExternalUrl(props.vacancy.url))
const badges = computed(() => vacancyBadges(props.vacancy))
const score = computed(() => scoreBadge(props.vacancy))
const reasonsOpen = ref(false)
const attribution = computed(() => sourceAttribution(props.vacancy.source))
const alsoFoundAt = computed(() => props.vacancy.alsoFoundAt ?? [])
const reasonsId = computed(() => `reasons-${props.vacancy.id}`)
const statusBadges = computed(() => badges.value.filter(b => b.variant !== 'employer' && b.variant !== 'intermediary'))
const employerBadge = computed(() => badges.value.find(b => b.variant === 'employer' || b.variant === 'intermediary'))
const starred = computed(() => props.vacancy.interest === 'Starred')
const dismissed = computed(() => props.vacancy.interest === 'Dismissed')
const dateLabel = computed(() => props.vacancy.publishedAt
  ? `Veröffentlicht ${formatRelativeDate(props.vacancy.publishedAt)}`
  : `Importiert ${formatRelativeDate(props.vacancy.fetchedAt)}`)
const dateTitle = computed(() => formatDate(props.vacancy.publishedAt ?? props.vacancy.fetchedAt))
</script>

<template>
  <article
    :id="`card-${vacancy.id}`"
    class="card"
    :class="{ 'card--seen': vacancy.isSeen, 'card--active': active, 'card--dismissed': dismissed }"
    :aria-labelledby="`vacancy-${vacancy.id}`"
    :aria-current="active ? 'true' : undefined"
    tabindex="-1"
    @focusin="emit('activate', vacancy)"
  >
    <div class="card__main">
      <div class="card__heading">
        <span class="score" :class="`score--${score.level.toLowerCase()}`" :title="score.description">
          {{ score.text }}<span class="visually-hidden"> – {{ score.description }}</span>
        </span>
        <span v-for="badge in statusBadges" :key="badge.variant" class="badge" :class="`badge--${badge.variant}`">
          {{ badge.label }}
        </span>
        <h2 :id="`vacancy-${vacancy.id}`" class="card__title">
          <NuxtLink :to="{ path: `/vacancies/${vacancy.id}`, query: listQuery }">
            <HighlightedText :text="vacancy.title" :keywords="highlightKeywords" />
          </NuxtLink>
        </h2>
      </div>
      <p class="card__meta">
        <span>{{ vacancy.company }}</span>
        <span v-if="vacancy.location"> · {{ vacancy.location }}</span>
      </p>
      <p class="card__source">
        <a v-if="attribution" :href="attribution.href" target="_blank" rel="noopener noreferrer">{{ attribution.label }}</a>
        <span v-if="alsoFoundAt.length > 0">
          · auch bei
          <template v-for="(link, index) in alsoFoundAt" :key="link.id">
            <NuxtLink :to="{ path: `/vacancies/${link.id}`, query: listQuery }">{{ sourceLabel(link.source) }}</NuxtLink><span v-if="index < alsoFoundAt.length - 1">, </span>
          </template>
        </span>
      </p>
      <p class="card__date">
        <time :datetime="vacancy.publishedAt ?? vacancy.fetchedAt" :title="dateTitle">{{ dateLabel }}</time>
        <button
          type="button"
          class="card__why"
          :aria-expanded="reasonsOpen"
          :aria-controls="reasonsId"
          @click="reasonsOpen = !reasonsOpen"
        >
          Warum?
        </button>
      </p>
      <div v-if="reasonsOpen" :id="reasonsId" class="card__reasons">
        <p v-if="scoreLine(vacancy.relevanceScore, vacancy.rankScore)" class="card__score-line">
          {{ scoreLine(vacancy.relevanceScore, vacancy.rankScore) }}
        </p>
        <VacancyReasons :reasons="vacancy.relevanceReasons" />
      </div>
    </div>

    <div class="card__side">
      <span
        v-if="employerBadge"
        class="badge"
        :class="{ 'badge--intermediary': employerBadge.variant === 'intermediary' }"
      >{{ employerBadge.label }}</span>
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
        <button
          type="button"
          class="button button--secondary button--small"
          :aria-pressed="starred"
          :title="starred ? 'Nicht mehr merken (m)' : 'Merken (m)'"
          @click="emit('toggle-star', vacancy)"
        >
          {{ starred ? '★ Gemerkt' : '☆ Merken' }}
        </button>
        <button
          type="button"
          class="button button--secondary button--small"
          :aria-pressed="dismissed"
          :title="dismissed ? 'Wieder einblenden (x)' : 'Ausblenden – nicht interessant (x)'"
          @click="emit('toggle-dismiss', vacancy)"
        >
          {{ dismissed ? 'Einblenden' : 'Ausblenden' }}
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
  scroll-margin: var(--space-6);

  &--seen {
    border-left-color: var(--color-border);
  }

  &--dismissed {
    opacity: 0.65;
  }

  // Keyboard navigation (j/k): clearly visible, also without :focus-visible.
  &--active,
  &:focus-visible {
    outline: 3px solid var(--color-focus);
    outline-offset: 2px;
  }

  &__main {
    min-width: 0;
  }

  &__heading {
    display: flex;
    flex-wrap: wrap;
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

  &__source {
    margin: var(--space-1) 0 0;
    font-size: 0.8rem;
    color: var(--color-muted);
  }

  &__date {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: var(--space-3);
    margin: var(--space-1) 0 0;
    font-size: 0.85rem;
    color: var(--color-muted);
  }

  &__why {
    font: inherit;
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--color-accent);
    text-decoration: underline;
    cursor: pointer;
  }

  &__score-line {
    margin: 0 0 var(--space-1);
    font-weight: 600;
    font-size: 0.85rem;
  }

  &__reasons {
    margin-top: var(--space-2);
    padding: var(--space-2) var(--space-3);
    background: var(--color-surface-muted);
    border-radius: var(--radius);
  }

  &__side {
    display: flex;
    flex-direction: column;
    align-items: flex-end;
    gap: var(--space-3);
    flex-shrink: 0;
    max-width: 22rem;
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: var(--space-2);
  }
}

@media (max-width: 720px) {
  .card {
    flex-direction: column;

    &__side {
      align-items: flex-start;
      max-width: none;
    }

    &__actions {
      justify-content: flex-start;
    }
  }
}
</style>
