<script setup lang="ts">
/**
 * „Passt" / „Passt nicht" (keys p / n): my judgement of a vacancy, the ground truth for the evaluation in step 07b.
 * Pressing the active one again removes the label. An automatic label (applied / Ausblenden, 07c) is shown as such;
 * pressing either button turns it into my own.
 */
const props = defineProps<{ vacancyId: string }>()
const label = defineModel<boolean | null>({ default: null })
const automatic = defineModel<boolean>('automatic', { default: false })

const api = useVacanciesApi()
const { refresh } = useLabelSummary()
const saving = ref(false)
const error = ref<string | null>(null)

async function choose(fits: boolean) {
  if (saving.value) {
    return
  }
  const next = label.value === fits && !automatic.value ? null : fits
  const previous = label.value
  const wasAutomatic = automatic.value
  label.value = next
  automatic.value = false
  saving.value = true
  error.value = null
  try {
    await api.setLabel(props.vacancyId, next)
    await refresh()
  }
  catch (e: unknown) {
    label.value = previous
    automatic.value = wasAutomatic
    error.value = `Bewertung nicht gespeichert: ${describeApiError(e)}`
  }
  finally {
    saving.value = false
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.ctrlKey || event.metaKey || event.altKey || event.isComposing || isTypingTarget(event.target)) {
    return
  }
  if (event.key === 'p' || event.key === 'n') {
    event.preventDefault()
    void choose(event.key === 'p')
  }
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="label" role="group" aria-label="Meine Bewertung">
    <button
      type="button"
      class="button label__button"
      :class="{ 'label__button--fits': label === true }"
      :aria-pressed="label === true"
      :disabled="saving"
      aria-keyshortcuts="p"
      @click="choose(true)"
    >
      👍 Passt <kbd>p</kbd>
    </button>
    <button
      type="button"
      class="button label__button"
      :class="{ 'label__button--not': label === false }"
      :aria-pressed="label === false"
      :disabled="saving"
      aria-keyshortcuts="n"
      @click="choose(false)"
    >
      👎 Passt nicht <kbd>n</kbd>
    </button>
    <span v-if="automatic && label != null" class="label__auto" title="Aus meinem Verhalten abgeleitet: beworben → Passt, ausgeblendet → Passt nicht">automatisch</span>
    <p v-if="error" class="label__error" role="alert">
      {{ error }}
    </p>
  </div>
</template>

<style scoped lang="scss">
.label {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;

  &__button--fits,
  &__button--not {
    font-weight: 600;
    outline: 2px solid currentColor;
  }

  &__error {
    color: var(--color-error-text);
  }

  &__auto {
    font-size: 0.8rem;
    color: var(--color-muted);
  }

  kbd {
    font-size: 0.75em;
    opacity: 0.7;
  }
}
</style>
