<script setup lang="ts">
const { lastImport } = useLastImport()
const { summary: labels, refresh: refreshLabels } = useLabelSummary()
onMounted(() => void refreshLabels())

// Re-render the relative time every minute.
const now = ref(new Date())
let clock: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  clock = setInterval(() => (now.value = new Date()), 60_000)
})
onBeforeUnmount(() => clearInterval(clock))

const lastImportLabel = computed(() => (lastImport.value?.finishedAt
  ? formatRelativeTime(lastImport.value.finishedAt, now.value)
  : null))
const newSinceLink = computed(() => (lastImport.value?.startedAt
  ? { path: '/', query: { since: lastImport.value.startedAt } }
  : null))
</script>

<template>
  <header class="header">
    <div class="header__inner">
      <NuxtLink to="/" class="header__brand">
        Job Autopilot
      </NuxtLink>
      <nav class="header__nav" aria-label="Hauptnavigation">
        <NuxtLink to="/" class="header__link" exact-active-class="header__link--active">
          Stellen
        </NuxtLink>
        <NuxtLink to="/bewerbungen" class="header__link" active-class="header__link--active">
          Bewerbungen
        </NuxtLink>
        <NuxtLink to="/profil" class="header__link" active-class="header__link--active">
          Profil
        </NuxtLink>
        <NuxtLink to="/suchprofil" class="header__link" active-class="header__link--active">
          Suchprofil
        </NuxtLink>
        <NuxtLink to="/statistik" class="header__link" active-class="header__link--active">
          Statistik
        </NuxtLink>
      </nav>
      <p v-if="lastImport" class="header__last">
        <span :title="lastImport.finishedAt ? formatDateTime(lastImport.finishedAt) : undefined">
          Letzter Import: {{ lastImportLabel }}
        </span>
        <NuxtLink v-if="newSinceLink && lastImport.added > 0" :to="newSinceLink" class="header__new">
          {{ lastImport.added }} neu seit letztem Import
        </NuxtLink>
      </p>
      <p v-if="labels" class="header__labels" title="Meine Bewertungen „Passt“ / „Passt nicht“ (Ziel für die Auswertung)">
        Bewertet: {{ labels.labeled }}<template v-if="labels.automatic"> + {{ labels.automatic }} automatisch</template> / {{ labels.target }}
      </p>
      <ImportButton />
    </div>
  </header>
</template>

<style scoped lang="scss">
.header {
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);

  &__inner {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-3);
    max-width: var(--content-width);
    margin: 0 auto;
    padding: var(--space-3) var(--space-4);
  }

  &__brand {
    font-size: 1.2rem;
    font-weight: 700;
    color: var(--color-text);
    text-decoration: none;
  }

  &__nav {
    display: flex;
    gap: var(--space-3);
  }

  &__link {
    color: var(--color-muted);
    text-decoration: none;
    font-weight: 600;

    &:hover,
    &--active {
      color: var(--color-accent);
      text-decoration: underline;
    }
  }

  &__labels {
    margin: 0;
    color: var(--color-muted);
    font-size: 0.9em;
  }

  &__last {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-1) var(--space-3);
    margin: 0 auto 0 0;
    color: var(--color-muted);
    font-size: 0.9rem;
  }

  &__new {
    font-weight: 600;
  }
}
</style>
