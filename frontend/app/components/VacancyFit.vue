<script setup lang="ts">
import type { FitResponse } from '~/types/vacancy'

const props = defineProps<{ fit: FitResponse }>()

const groups = computed(() => groupFit(props.fit.requirements))
const summary = computed(() => fitSummary(props.fit))
</script>

<template>
  <div class="fit">
    <p class="fit__summary">
      {{ summary }}
      <span v-if="!fit.hasDescription"> (nur der Titel geprüft – Beschreibung fehlt noch)</span>
    </p>

    <ul v-if="fit.warnings.length > 0" class="fit__warnings">
      <li v-for="warning in fit.warnings" :key="warning.text">
        ⚠ {{ warning.text }}<span v-if="warning.penalty > 0"> (−{{ warning.penalty }})</span>
      </li>
    </ul>

    <div class="fit__groups">
      <section aria-labelledby="fit-covered">
        <h3 id="fit-covered">
          <span aria-hidden="true">✓</span> Erfüllt
        </h3>
        <ul v-if="groups.covered.length > 0">
          <li v-for="item in groups.covered" :key="item.name">
            {{ item.name }} <span class="fit__note">{{ item.note }}</span>
            <span v-if="item.optional" class="fit__optional">optional</span>
          </li>
        </ul>
        <p v-else class="fit__empty">
          –
        </p>
      </section>
      <section aria-labelledby="fit-partial">
        <h3 id="fit-partial">
          <span aria-hidden="true">~</span> Teilweise
        </h3>
        <ul v-if="groups.partial.length > 0">
          <li v-for="item in groups.partial" :key="item.name">
            {{ item.name }} <span class="fit__note">{{ item.note }}</span>
            <span v-if="item.optional" class="fit__optional">optional</span>
          </li>
        </ul>
        <p v-else class="fit__empty">
          –
        </p>
      </section>
      <section aria-labelledby="fit-gaps">
        <h3 id="fit-gaps">
          <span aria-hidden="true">✗</span> Lücken
        </h3>
        <ul v-if="groups.gaps.length > 0">
          <li v-for="item in groups.gaps" :key="item.name">
            {{ item.name }}
            <span v-if="item.optional" class="fit__optional">optional</span>
            <span class="fit__phrase">{{ item.note }}</span>
          </li>
        </ul>
        <p v-else class="fit__empty">
          –
        </p>
      </section>
    </div>
  </div>
</template>

<style scoped lang="scss">
.fit {
  &__summary {
    margin: 0 0 var(--space-2);
    font-weight: 600;
  }

  &__warnings {
    margin: 0 0 var(--space-3);
    padding-left: var(--space-4);
    color: var(--color-warning-text);
  }

  &__groups {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: var(--space-4);

    h3 {
      margin: 0 0 var(--space-1);
      font-size: 0.95rem;
    }

    ul {
      margin: 0;
      padding-left: var(--space-4);
    }

    li {
      margin-bottom: var(--space-1);
    }
  }

  &__note,
  &__empty {
    color: var(--color-muted);
    font-size: 0.85rem;
  }

  &__phrase {
    display: block;
    color: var(--color-muted);
    font-size: 0.85rem;
    font-style: italic;
  }

  &__optional {
    margin-left: var(--space-1);
    padding: 0 0.4rem;
    border-radius: 999px;
    font-size: 0.75rem;
    background: var(--color-surface-muted);
    color: var(--color-muted);
  }
}

@media (max-width: 720px) {
  .fit__groups {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
