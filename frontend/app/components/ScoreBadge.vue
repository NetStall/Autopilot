<script setup lang="ts">
import type { Vacancy } from '~/types/vacancy'

const props = withDefaults(defineProps<{
  vacancy: Pick<Vacancy, 'relevanceScore' | 'relevanceLevel' | 'rankScore'>
  /** Show „Relevanz 94 · Abzüge −8" as a second line (detail page) instead of only as tooltip. */
  showDetail?: boolean
}>(), { showDetail: false })

const thresholds = useScoreThresholds()
const badge = computed(() => scoreBadge(props.vacancy, thresholds.value))
</script>

<template>
  <span class="score-badge">
    <span
      class="score"
      :class="`score--${badge.tone}`"
      role="img"
      :aria-label="badge.text === '–' ? badge.description : `${badge.description}, ${badge.detail}`"
      :title="badge.detail"
    >{{ badge.text }}</span>
    <span v-if="showDetail" class="score-badge__detail">{{ badge.detail }}</span>
  </span>
</template>

<style scoped lang="scss">
.score-badge {
  display: inline-flex;
  align-items: baseline;
  gap: var(--space-2);

  &__detail {
    font-size: 0.8rem;
    color: var(--color-muted);
  }
}
</style>
