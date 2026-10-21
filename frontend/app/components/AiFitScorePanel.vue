<script setup lang="ts">
import type { AiFitScore } from '~/types/extraction'

/** „KI-Bewertung" in „Warum?": score, verdict, three reasons and the blockers — it does not change the rank. */
defineProps<{ score: AiFitScore }>()
</script>

<template>
  <div class="ai">
    <p class="ai__head">
      KI-Bewertung: <strong>{{ score.score }}</strong> – {{ score.verdict }}
      <span class="ai__note">(lokales Modell, ändert den Rang nicht)</span>
    </p>
    <ul class="ai__reasons">
      <li v-for="reason in score.reasons" :key="reason">
        {{ reason }}
      </li>
    </ul>
    <p v-if="score.blockers.length" class="ai__blockers">
      Ausschlussgründe: {{ score.blockers.join(' · ') }}
    </p>
  </div>
</template>

<style scoped lang="scss">
.ai {
  margin-top: 0.75rem;

  &__note {
    color: var(--color-muted);
    font-size: 0.9em;
  }

  &__reasons {
    margin: 0.25rem 0;
    padding-left: 1.25rem;
  }

  &__blockers {
    color: var(--color-error-text);
  }
}
</style>
