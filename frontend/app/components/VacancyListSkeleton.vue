<script setup lang="ts">
withDefaults(defineProps<{ rows?: number }>(), { rows: 4 })
</script>

<template>
  <div class="skeleton" role="status">
    <span class="visually-hidden">Stellen werden geladen …</span>
    <div v-for="row in rows" :key="row" class="skeleton__card" aria-hidden="true">
      <div class="skeleton__line skeleton__line--title" />
      <div class="skeleton__line skeleton__line--meta" />
      <div class="skeleton__line skeleton__line--date" />
    </div>
  </div>
</template>

<style scoped lang="scss">
.skeleton {
  display: grid;
  gap: var(--space-3);

  &__card {
    padding: var(--space-4);
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
  }

  &__line {
    height: 0.9rem;
    border-radius: 4px;
    background: var(--color-skeleton);
    animation: pulse 1.4s ease-in-out infinite;

    & + & {
      margin-top: var(--space-2);
    }

    &--title {
      width: 55%;
      height: 1.1rem;
    }

    &--meta {
      width: 35%;
    }

    &--date {
      width: 20%;
    }
  }
}

@keyframes pulse {
  50% {
    opacity: 0.5;
  }
}

@media (prefers-reduced-motion: reduce) {
  .skeleton__line {
    animation: none;
  }
}
</style>
