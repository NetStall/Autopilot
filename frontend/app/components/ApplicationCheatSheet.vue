<script setup lang="ts">
import type { CheatSheetItem } from '~/types/documents'

defineProps<{ items: CheatSheetItem[] }>()

const copied = ref<string | null>(null)
const failed = ref(false)
let reset: ReturnType<typeof setTimeout> | undefined

async function copy(item: CheatSheetItem) {
  failed.value = false
  try {
    await navigator.clipboard.writeText(item.value)
    copied.value = item.key
    clearTimeout(reset)
    reset = setTimeout(() => (copied.value = null), 2000)
  }
  catch {
    failed.value = true
  }
}

onBeforeUnmount(() => clearTimeout(reset))
</script>

<template>
  <section class="sheet" aria-labelledby="sheet-heading">
    <h2 id="sheet-heading">
      Spickzettel fürs Bewerbungsformular
    </h2>
    <dl class="sheet__list">
      <div v-for="item in items" :key="item.key" class="sheet__row">
        <dt>{{ item.label }}</dt>
        <dd>
          <pre v-if="item.multiline" class="sheet__value sheet__value--multiline">{{ item.value }}</pre>
          <span v-else class="sheet__value">{{ item.value }}</span>
          <button
            type="button"
            class="button button--secondary button--small"
            :aria-label="`${item.label} kopieren`"
            @click="copy(item)"
          >
            {{ copied === item.key ? 'Kopiert ✓' : 'Kopieren' }}
          </button>
        </dd>
      </div>
    </dl>
    <p class="visually-hidden" aria-live="polite">
      {{ copied ? 'In die Zwischenablage kopiert.' : '' }}
    </p>
    <p v-if="failed" class="sheet__error" role="alert">
      Kopieren nicht möglich – bitte den Text markieren und kopieren.
    </p>
  </section>
</template>

<style scoped lang="scss">
.sheet {
  display: grid;
  gap: var(--space-2);

  h2 {
    margin: 0;
    font-size: 1.1rem;
  }

  &__list {
    display: grid;
    gap: var(--space-2);
    margin: 0;
  }

  &__row {
    display: grid;
    gap: var(--space-1);

    dt {
      font-size: 0.8rem;
      font-weight: 600;
      color: var(--color-muted);
    }

    dd {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2);
      margin: 0;
    }
  }

  &__value {
    flex: 1;
    overflow-wrap: anywhere;

    &--multiline {
      margin: 0;
      font: inherit;
      white-space: pre-wrap;
    }
  }

  &__error {
    margin: 0;
    color: var(--color-error-text);
  }
}
</style>
