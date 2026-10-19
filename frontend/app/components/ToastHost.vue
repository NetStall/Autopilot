<script setup lang="ts">
const { toasts, dismiss } = useToasts()
</script>

<template>
  <div class="toasts" aria-live="assertive" aria-atomic="false">
    <div v-for="toast in toasts" :key="toast.id" class="toast" :class="`toast--${toast.kind}`" role="status">
      <span>{{ toast.message }}</span>
      <button type="button" class="toast__close" aria-label="Meldung schließen" @click="dismiss(toast.id)">
        ×
      </button>
    </div>
  </div>
</template>

<style scoped lang="scss">
.toasts {
  position: fixed;
  right: var(--space-4);
  bottom: var(--space-4);
  left: var(--space-4);
  z-index: 20;
  display: grid;
  justify-items: end;
  gap: var(--space-2);
  pointer-events: none;
}

.toast {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  max-width: 28rem;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius);
  background: var(--color-text);
  color: var(--color-bg);
  box-shadow: 0 4px 16px rgb(0 0 0 / 20%);
  pointer-events: auto;

  &--error {
    background: var(--color-error-text);
    color: #fff;
  }

  &__close {
    font: inherit;
    font-size: 1.2rem;
    line-height: 1;
    border: 0;
    background: transparent;
    color: inherit;
    cursor: pointer;
  }
}
</style>
