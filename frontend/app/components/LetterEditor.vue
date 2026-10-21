<script setup lang="ts">
import type { DraftWarning, Letter } from '~/types/documents'

const props = withDefaults(defineProps<{
  warnings?: DraftWarning[]
  disabled?: boolean
  /** Index of the paragraph being rewritten right now. */
  rephrasing?: number | null
  /** Page count of the last rendered preview. */
  renderedPages?: number | null
  /** The signature under the closing (not editable here; from the profile). */
  senderName?: string
}>(), { warnings: () => [], disabled: false, rephrasing: null, renderedPages: null })

const emit = defineEmits<{ rephrase: [index: number] }>()

const letter = defineModel<Letter>({ required: true })

const length = computed(() => letterLength(letter.value, props.renderedPages))

function update(change: Partial<Letter>) {
  letter.value = { ...letter.value, ...change }
}

function setParagraph(index: number, text: string) {
  update({ paragraphs: letter.value.paragraphs.map((p, i) => (i === index ? text : p)) })
}

function paragraphWarnings(index: number) {
  return warningsForParagraph(props.warnings, index)
}
</script>

<template>
  <section class="letter" aria-labelledby="letter-heading">
    <div class="letter__head">
      <h2 id="letter-heading">
        Anschreiben
      </h2>
      <p class="letter__length" :class="{ 'letter__length--long': length.tooLong }" aria-live="polite">
        {{ length.label }}
      </p>
    </div>

    <label for="letter-subject">Betreff</label>
    <input
      id="letter-subject"
      class="input"
      :value="letter.subject"
      :disabled="disabled"
      maxlength="300"
      @input="update({ subject: ($event.target as HTMLInputElement).value })"
    >

    <label for="letter-salutation">Anrede</label>
    <input
      id="letter-salutation"
      class="input"
      :value="letter.salutation"
      :disabled="disabled"
      maxlength="300"
      @input="update({ salutation: ($event.target as HTMLInputElement).value })"
    >

    <div v-for="(paragraph, index) in letter.paragraphs" :key="index" class="letter__paragraph">
      <div class="letter__paragraph-head">
        <label :for="paragraphId(index)">Absatz {{ index + 1 }}</label>
        <span class="letter__count">{{ paragraph.length }} Zeichen</span>
        <button
          type="button"
          class="button button--secondary button--small"
          :disabled="disabled || rephrasing !== null"
          :aria-busy="rephrasing === index"
          @click="emit('rephrase', index)"
        >
          {{ rephrasing === index ? 'Wird neu formuliert …' : 'Absatz neu formulieren' }}
        </button>
      </div>
      <textarea
        :id="paragraphId(index)"
        class="input letter__text"
        :class="{ 'letter__text--warning': paragraphWarnings(index).some(w => w.severity === 'Warning') }"
        :value="paragraph"
        :disabled="disabled"
        :maxlength="MAX_PARAGRAPH_LENGTH"
        rows="5"
        :aria-describedby="paragraphWarnings(index).length > 0 ? `${paragraphId(index)}-warnings` : undefined"
        @input="setParagraph(index, ($event.target as HTMLTextAreaElement).value)"
      />
      <ul v-if="paragraphWarnings(index).length > 0" :id="`${paragraphId(index)}-warnings`" class="letter__warnings">
        <li v-for="warning in paragraphWarnings(index)" :key="warning.code + warning.message" :class="`letter__warning--${warning.severity.toLowerCase()}`">
          {{ warning.severity === 'Warning' ? 'Warnung' : 'Hinweis' }}: {{ warning.message }}
        </li>
      </ul>
    </div>

    <label for="letter-closing">Grußformel</label>
    <input
      id="letter-closing"
      class="input"
      :value="letter.closing"
      :disabled="disabled"
      maxlength="300"
      @input="update({ closing: ($event.target as HTMLInputElement).value })"
    >
    <p v-if="senderName" class="letter__signature" title="Unterschrift aus dem Profil – steht auch im PDF">
      {{ senderName }}
    </p>
  </section>
</template>

<style scoped lang="scss">
.letter {
  display: grid;
  gap: var(--space-2);

  &__signature {
    margin: 0;
    color: var(--color-muted);
  }

  h2 {
    margin: 0;
    font-size: 1.1rem;
  }

  label {
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__head,
  &__paragraph-head {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-2) var(--space-3);
  }

  &__head {
    justify-content: space-between;
  }

  &__length {
    margin: 0;
    font-size: 0.85rem;
    color: var(--color-muted);

    &--long {
      color: var(--color-error-text);
      font-weight: 600;
    }
  }

  &__paragraph {
    display: grid;
    gap: var(--space-1);
    margin-top: var(--space-2);
  }

  &__count {
    margin-right: auto;
    font-size: 0.8rem;
    color: var(--color-muted);
    font-variant-numeric: tabular-nums;
  }

  &__text {
    font: inherit;
    line-height: 1.45;
    resize: vertical;

    &--warning {
      border-color: var(--color-warning-text);
    }
  }

  &__warnings {
    margin: 0;
    padding-left: var(--space-4);
    font-size: 0.85rem;
  }

  &__warning--warning {
    color: var(--color-error-text);
  }

  &__warning--hint {
    color: var(--color-muted);
  }
}
</style>
