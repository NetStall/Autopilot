<script setup lang="ts">
const open = defineModel<boolean>('open', { default: false })
const dialog = ref<HTMLDialogElement | null>(null)

watch(open, (value) => {
  if (!dialog.value) {
    return
  }
  if (value && !dialog.value.open) {
    dialog.value.showModal()
  }
  else if (!value && dialog.value.open) {
    dialog.value.close()
  }
})
</script>

<template>
  <dialog ref="dialog" class="help" aria-labelledby="shortcut-help-title" @close="open = false">
    <h2 id="shortcut-help-title">
      Tastenkürzel
    </h2>
    <dl class="help__list">
      <template v-for="item in SHORTCUT_HELP" :key="item.keys">
        <dt><kbd>{{ item.keys }}</kbd></dt>
        <dd>{{ item.action }}</dd>
      </template>
    </dl>
    <p class="help__note">
      Kürzel sind abgeschaltet, solange ein Eingabefeld den Fokus hat.
    </p>
    <form method="dialog">
      <button type="submit" class="button">
        Schließen
      </button>
    </form>
  </dialog>
</template>

<style scoped lang="scss">
.help {
  max-width: 26rem;
  padding: var(--space-5);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-surface);
  color: var(--color-text);

  &::backdrop {
    background: rgb(0 0 0 / 40%);
  }

  h2 {
    margin: 0 0 var(--space-3);
    font-size: 1.2rem;
  }

  &__list {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: var(--space-2) var(--space-4);
    margin: 0 0 var(--space-3);

    dd {
      margin: 0;
    }
  }

  &__note {
    color: var(--color-muted);
    font-size: 0.9rem;
  }
}

kbd {
  display: inline-block;
  min-width: 1.6em;
  padding: 0.1em 0.4em;
  border: 1px solid var(--color-border);
  border-bottom-width: 2px;
  border-radius: 4px;
  background: var(--color-surface-muted);
  font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
  text-align: center;
}
</style>
