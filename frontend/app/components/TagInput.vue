<script setup lang="ts">
const props = withDefaults(defineProps<{
  id: string
  label: string
  /** Short help text below the field. */
  hint?: string
  placeholder?: string
}>(), { hint: '', placeholder: 'Eingeben und Enter drücken' })

const model = defineModel<string[]>({ required: true })
const draft = ref('')
const hintId = computed(() => `${props.id}-hint`)

function add() {
  const tags = parseTags(draft.value, model.value)
  if (tags.length > 0) {
    model.value = [...model.value, ...tags]
  }
  draft.value = ''
}

function remove(index: number) {
  model.value = model.value.filter((_, i) => i !== index)
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' || event.key === ',') {
    event.preventDefault()
    add()
  }
  else if (event.key === 'Backspace' && draft.value === '' && model.value.length > 0) {
    remove(model.value.length - 1)
  }
}
</script>

<template>
  <div class="tags">
    <label :for="id" class="tags__label">{{ label }}</label>
    <ul v-if="model.length > 0" class="tags__list" :aria-label="label">
      <li v-for="(tag, index) in model" :key="`${tag}-${index}`" class="tags__tag">
        {{ tag }}
        <button type="button" class="tags__remove" :aria-label="`${tag} entfernen`" @click="remove(index)">
          ×
        </button>
      </li>
    </ul>
    <input
      :id="id"
      v-model="draft"
      class="input"
      :placeholder="placeholder"
      :aria-describedby="hint ? hintId : undefined"
      autocomplete="off"
      @keydown="onKeydown"
      @blur="add"
    >
    <p v-if="hint" :id="hintId" class="tags__hint">
      {{ hint }}
    </p>
  </div>
</template>

<style scoped lang="scss">
.tags {
  display: grid;
  gap: var(--space-1);

  &__label {
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__list {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-1);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  &__tag {
    display: inline-flex;
    align-items: center;
    gap: var(--space-1);
    padding: 0.1rem 0.2rem 0.1rem 0.6rem;
    border-radius: 999px;
    background: var(--color-badge);
    color: var(--color-badge-text);
    font-size: 0.85rem;
  }

  &__remove {
    font: inherit;
    line-height: 1;
    padding: 0.1rem 0.4rem;
    border: 0;
    border-radius: 999px;
    background: transparent;
    color: inherit;
    cursor: pointer;

    &:hover,
    &:focus-visible {
      background: var(--color-surface);
    }
  }

  &__hint {
    margin: 0;
    font-size: 0.8rem;
    color: var(--color-muted);
  }
}
</style>
