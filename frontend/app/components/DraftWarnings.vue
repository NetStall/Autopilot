<script setup lang="ts">
import type { DraftWarning } from '~/types/documents'

const props = defineProps<{ warnings: DraftWarning[] }>()

const blocking = computed(() => props.warnings.filter(w => w.severity === 'Warning'))
const hints = computed(() => props.warnings.filter(w => w.severity === 'Hint'))
</script>

<template>
  <section class="warnings" aria-labelledby="warnings-heading">
    <h2 id="warnings-heading">
      Faktenprüfung
    </h2>
    <p v-if="warnings.length === 0" class="warnings__ok" role="status">
      Keine Auffälligkeiten – trotzdem selbst lesen.
    </p>
    <ul v-if="blocking.length > 0" class="warnings__list" aria-label="Warnungen">
      <li v-for="warning in blocking" :key="warning.code + warning.message" class="warnings__item warnings__item--warning">
        <strong>Warnung:</strong> {{ warning.message }}
        <a v-if="warning.paragraph !== null" :href="`#${paragraphId(warning.paragraph)}`">zu Absatz {{ warning.paragraph + 1 }}</a>
      </li>
    </ul>
    <ul v-if="hints.length > 0" class="warnings__list" aria-label="Hinweise">
      <li v-for="hint in hints" :key="hint.code + hint.message" class="warnings__item">
        <strong>Hinweis:</strong> {{ hint.message }}
        <a v-if="hint.paragraph !== null" :href="`#${paragraphId(hint.paragraph)}`">zu Absatz {{ hint.paragraph + 1 }}</a>
      </li>
    </ul>
  </section>
</template>

<style scoped lang="scss">
.warnings {
  display: grid;
  gap: var(--space-2);

  h2 {
    margin: 0;
    font-size: 1.1rem;
  }

  &__ok {
    margin: 0;
    color: var(--color-muted);
  }

  &__list {
    display: grid;
    gap: var(--space-1);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  &__item {
    padding: var(--space-2) var(--space-3);
    border-radius: var(--radius);
    background: var(--color-surface-muted);
    font-size: 0.9rem;

    &--warning {
      background: var(--color-warning-bg);
      color: var(--color-warning-text);
    }

    a {
      margin-left: var(--space-2);
    }
  }
}
</style>
