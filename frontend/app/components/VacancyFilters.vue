<script setup lang="ts">
import type { ConfiguredSearch, EmployerType, RelevanceLevel, RequiredGerman, VacancyFacets, VacancySort, VacancySource } from '~/types/vacancy'
import type { RemoteFilter, StatusFilter, VacancyFilters } from '~/utils/vacancyFilters'

const props = withDefaults(defineProps<{
  filters: VacancyFilters
  /** Configured searches for the „Suche" select; empty while loading. */
  searches?: ConfiguredSearch[]
  /** Counts per option under the other active filters. */
  facets?: VacancyFacets | null
  /** „Mindestbewertung" of the Suchprofil, used while the URL has none. */
  defaultMinScore?: number
  /** N of „Nur Top N" from the Suchprofil. */
  topN?: number
  /** „Deutsch erforderlich" of the Suchprofil, used while the URL has none. */
  defaultGerman?: RequiredGerman[]
}>(), { searches: () => [], facets: null, defaultMinScore: 0, topN: 20, defaultGerman: () => [] })

const minScore = computed(() => effectiveMinScore(props.filters, { minScore: props.defaultMinScore }))
const german = computed(() => effectiveGerman(props.filters, { minScore: 0, german: props.defaultGerman }))

/** The Suchprofil's default is not written to the URL; all levels = no restriction. */
function onGermanToggle(level: RequiredGerman, event: Event) {
  const next = toggleGerman(german.value, level, (event.target as HTMLInputElement).checked)
  if (next.length === 0) {
    (event.target as HTMLInputElement).checked = true // at least one level stays selected
    return
  }
  update({ german: sameGerman(next, props.defaultGerman) ? null : next })
}

function count(value: number | undefined): string {
  return countSuffix(props.facets, value)
}

function remoteCount(option: RemoteFilter): string {
  return countSuffix(props.facets, props.facets ? remoteFacetCount(props.facets.remoteLevel, option) : undefined)
}

/** Slider and number input share one value; the default of the Suchprofil is not written to the URL. */
function onMinScore(event: Event) {
  const value = normalizeMinScore(Number((event.target as HTMLInputElement).value))
  update({ minScore: value === props.defaultMinScore ? null : value })
}

function onTop(event: Event) {
  update({ top: (event.target as HTMLInputElement).checked ? props.topN : null })
}

function onSourceToggle(source: VacancySource, event: Event) {
  const selected = new Set(props.filters.sources)
  if ((event.target as HTMLInputElement).checked) {
    selected.add(source)
  }
  else {
    selected.delete(source)
  }
  update({ sources: VACANCY_SOURCES.filter(s => selected.has(s)) })
}

const emit = defineEmits<{
  /** `replace`: update the URL without a new history entry (used while typing). */
  change: [filters: VacancyFilters, options: { replace: boolean }]
}>()

const TEXT_DEBOUNCE_MS = 300

const statusOptions: { value: StatusFilter, label: string }[] = [
  { value: 'all', label: 'Alle' },
  { value: 'new', label: 'Neu' },
  { value: 'seen', label: 'Gesehen' },
]

const sortOptions: { value: VacancySort, label: string }[] = [
  { value: 'relevance', label: 'Relevanz' },
  { value: 'published', label: 'Neueste Veröffentlichung' },
  { value: 'fetched', label: 'Zuletzt importiert' },
]

const remoteOptions: { value: RemoteFilter, label: string }[] = [
  { value: 'any', label: 'Alle' },
  { value: 'remote', label: 'Remote oder hybrid' },
  { value: 'full', label: 'Nur voll remote' },
]

// Text fields are edited locally and pushed to the URL debounced.
const q = ref(props.filters.q)
const location = ref(props.filters.location)

// Back/forward or a reset changes the URL: take the new values unless the user is typing the same text.
watch(() => props.filters.q, value => {
  if (value !== q.value.trim()) {
    q.value = value
  }
})
watch(() => props.filters.location, value => {
  if (value !== location.value.trim()) {
    location.value = value
  }
})

function applyText() {
  if (q.value.trim() === props.filters.q && location.value.trim() === props.filters.location) {
    return
  }
  emit('change', { ...props.filters, q: q.value, location: location.value, page: 1 }, { replace: true })
}

const applyTextDebounced = debounce(applyText, TEXT_DEBOUNCE_MS)
watch([q, location], () => applyTextDebounced())
onBeforeUnmount(() => applyTextDebounced.cancel())

function submit() {
  applyTextDebounced.cancel()
  applyText()
}

function update(patch: Partial<VacancyFilters>) {
  emit('change', { ...props.filters, ...patch, page: 1 }, { replace: false })
}

function toggleEmployerType(type: EmployerType, checked: boolean) {
  const selected = new Set(props.filters.employerTypes)
  if (checked) {
    selected.add(type)
  }
  else {
    selected.delete(type)
  }
  update({ employerTypes: EMPLOYER_TYPES.filter(t => selected.has(t)) })
}

function onCheckboxChange(type: EmployerType, event: Event) {
  toggleEmployerType(type, (event.target as HTMLInputElement).checked)
}

function onSearchChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  update({ searches: value ? [value] : [] })
}

function onFlagChange(flag: 'starredOnly' | 'showDismissed', event: Event) {
  update({ [flag]: (event.target as HTMLInputElement).checked })
}

function onRemoteChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  update({ remote: value === 'full' || value === 'remote' ? value : 'any' })
}

/** At least one level stays selected: unticking the last one is ignored. */
function onRelevanceChange(level: RelevanceLevel, event: Event) {
  const input = event.target as HTMLInputElement
  const selected = new Set(props.filters.relevance)
  if (input.checked) {
    selected.add(level)
  }
  else {
    selected.delete(level)
  }
  if (selected.size === 0) {
    input.checked = true
    return
  }
  update({ relevance: RELEVANCE_LEVELS.filter(l => selected.has(l)) })
}


/** A search name from the URL that is no longer configured still shows up, so the filter stays visible. */
const searchOptions = computed(() => {
  const names = new Set(props.searches.map(s => s.name))
  const unknown = props.filters.searches.filter(name => !names.has(name))
  return [
    ...props.searches.map(s => ({ value: s.name, label: searchLabel(s) })),
    ...unknown.map(name => ({ value: name, label: name })),
  ]
})

function searchLabel(search: ConfiguredSearch): string {
  if (search.source === 'Himalayas') {
    return `${search.name} (${search.keyword}, Himalayas)`
  }
  if (search.remoteOnly) {
    return `${search.name} (${search.keyword}, remote)`
  }
  const where = search.location
    ? `${search.location}${search.radiusKm != null ? ` +${search.radiusKm} km` : ''}`
    : 'Deutschland'
  return `${search.name} (${search.keyword}, ${where})`
}

function onSortChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  update({ sort: value === 'fetched' || value === 'published' ? value : 'relevance' })
}

function reset() {
  applyTextDebounced.cancel()
  q.value = ''
  location.value = ''
  emit('change', { ...defaultFilters(), sort: props.filters.sort }, { replace: false })
}

const canReset = computed(() => hasActiveFilters(props.filters) || q.value.trim() !== '' || location.value.trim() !== '')
</script>

<template>
  <form class="filters" role="search" aria-label="Stellen filtern" @submit.prevent="submit">
    <div class="filters__field">
      <label for="filter-q">Stichwort</label>
      <input
        id="filter-q"
        v-model="q"
        class="input"
        type="search"
        name="q"
        placeholder="Titel oder Firma"
        autocomplete="off"
      >
    </div>

    <div class="filters__field">
      <label for="filter-location">Ort</label>
      <input
        id="filter-location"
        v-model="location"
        class="input"
        type="search"
        name="location"
        placeholder="z. B. Regensburg"
        autocomplete="off"
      >
    </div>

    <div class="filters__field">
      <label for="filter-sort">Sortierung</label>
      <select id="filter-sort" class="input" :value="filters.sort" @change="onSortChange">
        <option v-for="option in sortOptions" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
    </div>

    <div class="filters__field">
      <label for="filter-search">Suche</label>
      <select id="filter-search" class="input" :value="filters.searches[0] ?? ''" @change="onSearchChange">
        <option value="">
          Alle Suchen
        </option>
        <option v-for="option in searchOptions" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
    </div>

    <div class="filters__field">
      <label for="filter-remote">Arbeitsort</label>
      <select id="filter-remote" class="input" :value="filters.remote" @change="onRemoteChange">
        <option v-for="option in remoteOptions" :key="option.value" :value="option.value">
          {{ option.label }}{{ remoteCount(option.value) }}
        </option>
      </select>
    </div>

    <div class="filters__field filters__score">
      <label for="filter-min-score">Mindestbewertung</label>
      <div class="filters__score-inputs">
        <input
          id="filter-min-score"
          type="range"
          min="0"
          max="100"
          step="5"
          :value="minScore"
          aria-describedby="filter-min-score-hint"
          @change="onMinScore"
        >
        <input
          class="input filters__score-number"
          type="number"
          min="0"
          max="100"
          step="5"
          :value="minScore"
          aria-label="Mindestbewertung als Zahl"
          @change="onMinScore"
        >
      </div>
      <span id="filter-min-score-hint" class="filters__hint">
        {{ filters.minScore == null ? `Standard aus dem Suchprofil (${defaultMinScore})` : 'Eigener Wert' }}
      </span>
      <label class="filters__choice">
        <input type="checkbox" name="top" :checked="filters.top != null" @change="onTop">
        Nur Top {{ filters.top ?? topN }}
      </label>
    </div>

    <fieldset class="filters__group filters__group--inline">
      <legend>Quelle</legend>
      <label v-for="source in VACANCY_SOURCES" :key="source" class="filters__choice">
        <input
          type="checkbox"
          name="source"
          :value="source"
          :checked="filters.sources.includes(source)"
          @change="onSourceToggle(source, $event)"
        >
        {{ sourceLabel(source) }}{{ count(facets?.source[source]) }}
      </label>
    </fieldset>

    <fieldset class="filters__group filters__group--inline">
      <legend>Relevanz</legend>
      <label v-for="level in RELEVANCE_LEVELS" :key="level" class="filters__choice">
        <input
          type="checkbox"
          name="relevance"
          :value="level"
          :checked="filters.relevance.includes(level)"
          @change="onRelevanceChange(level, $event)"
        >
        {{ relevanceLabel(level) }}{{ count(facets?.relevanceLevel[level]) }}
      </label>
    </fieldset>

    <fieldset class="filters__group">
      <legend>Deutsch erforderlich</legend>
      <label v-for="level in REQUIRED_GERMAN" :key="level" class="filters__choice">
        <input
          type="checkbox"
          name="german"
          :value="level"
          :checked="german.length === 0 || german.includes(level)"
          @change="onGermanToggle(level, $event)"
        >
        {{ requiredGermanLabel(level) }}{{ count(facets?.requiredGerman?.[level]) }}
      </label>
    </fieldset>

    <fieldset class="filters__group">
      <legend>Arbeitgeber</legend>
      <label v-for="type in EMPLOYER_TYPES" :key="type" class="filters__choice">
        <input
          type="checkbox"
          :value="type"
          :checked="filters.employerTypes.includes(type)"
          @change="onCheckboxChange(type, $event)"
        >
        {{ employerTypeLabel(type) }}{{ count(facets?.employerType[type]) }}
      </label>
    </fieldset>

    <fieldset class="filters__group">
      <legend>Status</legend>
      <label v-for="option in statusOptions" :key="option.value" class="filters__choice">
        <input
          type="radio"
          name="status"
          :value="option.value"
          :checked="filters.status === option.value"
          @change="update({ status: option.value })"
        >
        {{ option.label }}
      </label>
      <label class="filters__choice">
        <input type="checkbox" name="starred" :checked="filters.starredOnly" @change="onFlagChange('starredOnly', $event)">
        ★ Gemerkt
      </label>
      <label class="filters__choice">
        <input
          type="checkbox"
          name="dismissed"
          :checked="filters.showDismissed"
          :disabled="filters.starredOnly"
          @change="onFlagChange('showDismissed', $event)"
        >
        Ausgeblendete anzeigen
      </label>
    </fieldset>

    <div class="filters__actions">
      <button
        v-if="filters.since"
        type="button"
        class="filters__chip"
        :title="`Nur Stellen, die seit ${formatDateTime(filters.since)} importiert wurden`"
        @click="update({ since: '' })"
      >
        Neu seit letztem Import <span aria-hidden="true">×</span>
        <span class="visually-hidden">(Filter entfernen)</span>
      </button>
      <button type="button" class="button button--secondary button--small" :disabled="!canReset" @click="reset">
        Filter zurücksetzen
      </button>
    </div>
  </form>
</template>

<style scoped lang="scss">
.filters {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--space-3) var(--space-4);
  padding: var(--space-4);
  margin-bottom: var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);

  label,
  legend {
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__field {
    display: grid;
    gap: var(--space-1);

    &--checkbox {
      align-content: end;
      padding-bottom: var(--space-2);
    }
  }

  &__group {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2) var(--space-4);
    margin: 0;
    padding: 0;
    border: 0;
    grid-column: span 3;

    &--inline {
      grid-column: span 2;
    }

    legend {
      width: 100%;
      margin-bottom: var(--space-1);
      padding: 0;
    }
  }

  &__choice {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    font-weight: 400 !important;
    font-size: 0.95rem !important;
    cursor: pointer;

    input {
      width: 1.1rem;
      height: 1.1rem;
      margin: 0;
      accent-color: var(--color-accent);
    }
  }

  &__score-inputs {
    display: flex;
    align-items: center;
    gap: var(--space-2);

    input[type='range'] {
      flex: 1;
      accent-color: var(--color-accent);
    }
  }

  &__score-number {
    width: 4.5rem;
  }

  &__hint {
    font-size: 0.8rem;
    color: var(--color-muted);
  }

  &__actions {
    grid-column: span 3;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: flex-end;
    gap: var(--space-2);
  }

  &__chip {
    font: inherit;
    font-size: 0.85rem;
    padding: var(--space-1) var(--space-3);
    border: 1px solid var(--color-accent);
    border-radius: 999px;
    background: var(--color-badge);
    color: var(--color-badge-text);
    cursor: pointer;
  }
}

@media (max-width: 720px) {
  .filters {
    grid-template-columns: minmax(0, 1fr);

    &__group,
    &__actions {
      grid-column: auto;
    }

    &__actions {
      justify-content: flex-start;
    }
  }
}
</style>
