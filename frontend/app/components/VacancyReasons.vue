<script setup lang="ts">
defineProps<{
  /** From the backend; shown as text only. */
  reasons: readonly string[]
}>()
</script>

<template>
  <ul v-if="reasons.length > 0" class="reasons">
    <li v-for="(reason, index) in reasons" :key="index" class="reasons__item" :class="`reasons__item--${reasonKind(reason)}`">
      {{ reason }}
    </li>
  </ul>
  <p v-else class="reasons__empty">
    Noch nicht bewertet – „Neu bewerten" oder der nächste Import holt das nach.
  </p>
</template>

<style scoped lang="scss">
.reasons {
  display: grid;
  gap: var(--space-1);
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;

  &__item {
    padding-left: var(--space-3);
    border-left: 3px solid var(--color-border);

    &--positive {
      border-left-color: var(--color-new-text);
    }

    &--negative {
      border-left-color: var(--color-error-text);
    }

    &--softened {
      border-left-color: var(--color-warning-text);
    }
  }

  &__empty {
    margin: 0;
    color: var(--color-muted);
    font-style: italic;
  }
}
</style>
