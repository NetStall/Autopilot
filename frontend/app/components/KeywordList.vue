<script setup lang="ts">
import type { StackKeyword } from '~/types/searchProfile'

const props = defineProps<{
  id: string
  label: string
  hint: string
  kind: 'mustHave' | 'niceToHave' | 'exclude'
}>()

const model = defineModel<StackKeyword[]>({ required: true })
const draft = ref('')
const openIndex = ref<number | null>(null)
const maxWeight = computed(() => MAX_WEIGHTS[props.kind])

function add() {
  const names = parseTags(draft.value, model.value.map(k => k.name))
  if (names.length > 0) {
    model.value = [...model.value, ...names.map(n => newKeyword(n, DEFAULT_WEIGHTS[props.kind]))]
  }
  draft.value = ''
}

function remove(index: number) {
  model.value = model.value.filter((_, i) => i !== index)
  openIndex.value = null
}

function patch(index: number, change: Partial<StackKeyword>) {
  model.value = model.value.map((k, i) => (i === index ? { ...k, ...change } : k))
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' || event.key === ',') {
    event.preventDefault()
    add()
  }
}
</script>

<template>
  <div class="keywords">
    <label :for="id" class="keywords__label">{{ label }}</label>
    <p class="keywords__hint">
      {{ hint }}
    </p>
    <ul class="keywords__list">
      <li v-for="(keyword, index) in model" :key="`${keyword.name}-${index}`" class="keywords__item">
        <button
          type="button"
          class="keywords__tag"
          :class="`keywords__tag--${kind}`"
          :aria-expanded="openIndex === index"
          :aria-controls="`${id}-edit-${index}`"
          @click="openIndex = openIndex === index ? null : index"
        >
          {{ keyword.name }} <span class="keywords__weight">{{ kind === 'exclude' ? '−' : '+' }}{{ keyword.weight }}</span>
        </button>
        <div v-if="openIndex === index" :id="`${id}-edit-${index}`" class="keywords__edit">
          <label :for="`${id}-w-${index}`">Gewicht (1–{{ maxWeight }}, im Titel doppelt)</label>
          <input
            :id="`${id}-w-${index}`"
            class="input"
            type="number"
            min="1"
            :max="maxWeight"
            :value="keyword.weight"
            @change="patch(index, { weight: Number(($event.target as HTMLInputElement).value) })"
          >
          <TagInput
            :id="`${id}-a-${index}`"
            :model-value="keyword.aliases"
            label="Andere Schreibweisen"
            hint="z. B. dotnet, ASP.NET Core"
            @update:model-value="patch(index, { aliases: $event })"
          />
          <label :for="`${id}-g-${index}`">Gruppe (teilt die Obergrenze, z. B. dotnet)</label>
          <input
            :id="`${id}-g-${index}`"
            class="input"
            :value="keyword.group ?? ''"
            @change="patch(index, { group: ($event.target as HTMLInputElement).value.trim() || null })"
          >
          <label class="keywords__check">
            <input type="checkbox" :checked="keyword.titleOnly" @change="patch(index, { titleOnly: ($event.target as HTMLInputElement).checked })">
            Nur im Titel zählen
          </label>
          <label class="keywords__check">
            <input type="checkbox" :checked="keyword.matchCase" @change="patch(index, { matchCase: ($event.target as HTMLInputElement).checked })">
            Groß-/Kleinschreibung beachten (z. B. REST, SAP)
          </label>
          <label v-if="kind === 'mustHave'" class="keywords__check">
            <input type="checkbox" :checked="keyword.core" @change="patch(index, { core: ($event.target as HTMLInputElement).checked })">
            Kern-Stack (schwächt Ausschlüsse daneben ab, „Java oder C#")
          </label>
          <label v-if="kind === 'exclude'" class="keywords__check">
            <input type="checkbox" :checked="keyword.softenedByCore" @change="patch(index, { softenedByCore: ($event.target as HTMLInputElement).checked })">
            Neben dem Kern-Stack nur abgeschwächt
          </label>
          <button type="button" class="button button--secondary button--small" @click="remove(index)">
            „{{ keyword.name }}" entfernen
          </button>
        </div>
      </li>
    </ul>
    <input
      :id="id"
      v-model="draft"
      class="input"
      placeholder="Begriff eingeben und Enter drücken"
      autocomplete="off"
      @keydown="onKeydown"
      @blur="add"
    >
  </div>
</template>

<style scoped lang="scss">
.keywords {
  display: grid;
  gap: var(--space-1);

  &__label {
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__hint {
    margin: 0;
    font-size: 0.8rem;
    color: var(--color-muted);
  }

  &__list {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-1);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  &__item {
    display: contents;
  }

  &__tag {
    font: inherit;
    font-size: 0.85rem;
    padding: 0.1rem 0.6rem;
    border: 1px solid transparent;
    border-radius: 999px;
    background: var(--color-badge);
    color: var(--color-badge-text);
    cursor: pointer;

    &--exclude {
      background: var(--color-warning-bg);
      color: var(--color-warning-text);
    }

    &[aria-expanded='true'] {
      border-color: currentcolor;
    }
  }

  &__weight {
    font-variant-numeric: tabular-nums;
    opacity: 0.8;
  }

  &__edit {
    flex-basis: 100%;
    display: grid;
    gap: var(--space-1);
    padding: var(--space-3);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
    background: var(--color-surface-muted);

    label {
      font-size: 0.85rem;
    }

    button {
      justify-self: start;
    }
  }

  &__check {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
  }
}
</style>
