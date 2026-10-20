<script setup lang="ts">
import type { RescoreStatus } from '~/types/vacancy'
import type { SearchProfile, SearchProfilePreview, SearchProfileResponse, SearchProfileRevision } from '~/types/searchProfile'

useHead({ title: 'Suchprofil · Job Autopilot' })

const PREVIEW_DELAY_MS = 600
const POLL_INTERVAL_MS = 1000

const api = useVacanciesApi()
const toasts = useToasts()
const importVersion = useImportVersion()

const loaded = ref<SearchProfileResponse | null>(null)
const form = ref<SearchProfile | null>(null)
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveErrors = ref<string[]>([])
const conflict = ref(false)
const preview = ref<SearchProfilePreview | null>(null)
const previewError = ref<string | null>(null)
const rescoring = ref(false)
const offerImport = ref(false)
const history = ref<SearchProfileRevision[]>([])
const importMessage = ref<string | null>(null)
let pollTimer: ReturnType<typeof setTimeout> | undefined

const dirty = computed(() => form.value != null && isDirty(form.value, loaded.value?.profile))
const errors = computed(() => (saveErrors.value.length > 0 ? saveErrors.value : preview.value?.errors ?? []))
const searches = computed(() => (dirty.value && preview.value ? preview.value.searches : loaded.value?.searches ?? []))

function apply(response: SearchProfileResponse) {
  loaded.value = response
  form.value = cloneProfile(response.profile)
  saveErrors.value = []
  conflict.value = false
}

async function load() {
  loadError.value = null
  try {
    apply(await api.searchProfile())
    await loadHistory()
  }
  catch (error: unknown) {
    loadError.value = describeApiError(error)
  }
}

async function loadHistory() {
  history.value = await api.searchProfileHistory().catch(() => [])
}

const runPreview = debounce(async (profile: SearchProfile) => {
  try {
    preview.value = await api.previewSearchProfile(profile)
    previewError.value = null
  }
  catch (error: unknown) {
    previewError.value = describeApiError(error)
  }
}, PREVIEW_DELAY_MS)

watch(form, (profile) => {
  saveErrors.value = []
  if (profile && dirty.value) {
    runPreview(cloneProfile(profile))
  }
  else {
    runPreview.cancel()
    preview.value = null
  }
}, { deep: true })

/** 400 ValidationProblem → the German messages under „profile". */
function validationMessages(error: unknown): string[] {
  const data = (error as { data?: { errors?: Record<string, string[]> } }).data
  return data?.errors?.profile ?? []
}

async function pollRescore() {
  clearTimeout(pollTimer)
  let status: RescoreStatus
  try {
    status = await api.rescoreStatus()
  }
  catch {
    rescoring.value = false
    return
  }
  if (status.state !== 'Idle') {
    pollTimer = setTimeout(() => void pollRescore(), POLL_INTERVAL_MS)
    return
  }
  rescoring.value = false
  if (status.lastError) {
    toasts.error(status.lastError)
  }
  else {
    toasts.show(`${status.lastScored ?? 0} Stellen mit dem neuen Suchprofil bewertet.`)
  }
}

function afterSave(response: SearchProfileResponse, rescoreState: RescoreStatus['state']) {
  apply(response)
  invalidateListDefaults()
  offerImport.value = true
  void loadHistory()
  if (rescoreState !== 'Idle') {
    rescoring.value = true
    void pollRescore()
  }
}

async function save() {
  if (!form.value || !loaded.value) {
    return
  }
  saving.value = true
  saveErrors.value = []
  try {
    const result = await api.saveSearchProfile(loaded.value.version, form.value)
    afterSave(result.searchProfile, result.rescore.state)
    toasts.show(`Suchprofil gespeichert (Version ${result.searchProfile.version}).`)
  }
  catch (error: unknown) {
    const status = errorStatus(error)
    if (status === 409) {
      conflict.value = true
    }
    else if (status === 400) {
      saveErrors.value = validationMessages(error)
    }
    else {
      toasts.error(`Speichern fehlgeschlagen: ${describeApiError(error)}`)
    }
  }
  finally {
    saving.value = false
  }
}

function discard() {
  if (loaded.value) {
    form.value = cloneProfile(loaded.value.profile)
  }
}

async function rollback(version: number) {
  if (!loaded.value) {
    return
  }
  if (dirty.value && !window.confirm('Ungespeicherte Änderungen gehen verloren. Trotzdem wiederherstellen?')) {
    return
  }
  try {
    const result = await api.rollbackSearchProfile(version, loaded.value.version)
    afterSave(result.searchProfile, result.rescore.state)
    toasts.show(`Version ${version} wiederhergestellt (jetzt Version ${result.searchProfile.version}).`)
  }
  catch (error: unknown) {
    if (errorStatus(error) === 409) {
      conflict.value = true
    }
    else {
      toasts.error(`Wiederherstellen fehlgeschlagen: ${describeApiError(error)}`)
    }
  }
}

async function startImport() {
  try {
    await api.startImport()
    offerImport.value = false
    importVersion.value++ // the header's import button follows the running import
  }
  catch (error: unknown) {
    toasts.error(`Import fehlgeschlagen: ${describeApiError(error)}`)
  }
}

async function onImportFile(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) {
    return
  }
  importMessage.value = null
  const parsed = parseImportedProfile(await file.text())
  if ('error' in parsed) {
    importMessage.value = parsed.error
    return
  }
  try {
    const checked = await api.previewSearchProfile(parsed.profile)
    if (checked.errors.length > 0) {
      importMessage.value = `Nicht übernommen: ${checked.errors.join(' ')}`
      return
    }
  }
  catch (error: unknown) {
    importMessage.value = `Nicht übernommen: ${describeApiError(error)}`
    return
  }
  form.value = cloneProfile(parsed.profile)
  importMessage.value = `„${file.name}" geladen – noch nicht gespeichert.`
}

function addTerm() {
  form.value?.searchTerms.push({ text: '', scope: 'Both' })
}

function addPlace() {
  form.value?.places.push({ city: '', radiusKm: 50 })
}

function numberOrNull(value: string): number | null {
  return value.trim() === '' ? null : Number(value)
}

function searchLabel(search: SearchProfileResponse['searches'][number]): string {
  const where = search.remoteOnly ? 'remote, Deutschland' : `${search.location ?? 'Deutschland'}${search.radiusKm ? ` +${search.radiusKm} km` : ''}`
  return `${search.keyword} · ${where} · ${sourceLabel(search.source)} · ${search.publishedWithinDays} Tage`
}

const LEAVE_QUESTION = 'Es gibt ungespeicherte Änderungen am Suchprofil. Seite trotzdem verlassen?'

onBeforeRouteLeave(() => (!dirty.value || window.confirm(LEAVE_QUESTION)))

function onBeforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) {
    event.preventDefault()
  }
}

onMounted(() => {
  window.addEventListener('beforeunload', onBeforeUnload)
  void load()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', onBeforeUnload)
  runPreview.cancel()
  clearTimeout(pollTimer)
})
</script>

<template>
  <main class="sp">
    <div class="sp__header">
      <h1>Suchprofil</h1>
      <span v-if="loaded" class="sp__version">Version {{ loaded.version }}<template v-if="loaded.updatedAt"> · gespeichert {{ formatDateTime(loaded.updatedAt) }}</template></span>
    </div>
    <p class="sp__hint">
      Was ich suche: Suchbegriffe, Orte, Stack und Gewichtung. Wer ich bin (Fähigkeiten, Sprachen, Erfahrung) steht im
      <NuxtLink to="/profil">Profil</NuxtLink>.
    </p>

    <div v-if="loadError" class="state state--error" role="alert">
      Das Suchprofil konnte nicht geladen werden. {{ loadError }}
      <button type="button" class="button button--secondary button--small" @click="load">
        Erneut versuchen
      </button>
    </div>
    <div v-else-if="!form" class="state" role="status">
      Suchprofil wird geladen …
    </div>

    <form v-else class="sp__form" novalidate @submit.prevent="save">
      <div class="sp__bar" role="region" aria-label="Speichern und Vorschau">
        <p class="sp__preview" aria-live="polite">
          <template v-if="!dirty">
            Keine ungespeicherten Änderungen.
          </template>
          <template v-else-if="previewError">
            Vorschau nicht verfügbar: {{ previewError }}
          </template>
          <template v-else-if="preview && preview.errors.length === 0">
            {{ previewLine(preview.matching, preview.fullRemote, preview.currentMatching) }}
          </template>
          <template v-else-if="preview">
            Bitte die markierten Angaben korrigieren.
          </template>
          <template v-else>
            Vorschau wird berechnet …
          </template>
        </p>
        <div class="sp__actions">
          <button type="button" class="button button--secondary" :disabled="!dirty || saving" @click="discard">
            Verwerfen
          </button>
          <button type="submit" class="button" :disabled="!dirty || saving || errors.length > 0" :aria-busy="saving">
            {{ saving ? 'Wird gespeichert …' : 'Speichern' }}
          </button>
        </div>
      </div>

      <div v-if="conflict" class="state state--error" role="alert">
        Das Suchprofil wurde inzwischen an anderer Stelle gespeichert. Neu laden verwirft die Änderungen hier.
        <button type="button" class="button button--secondary button--small" @click="load">
          Neu laden
        </button>
      </div>
      <ul v-if="errors.length > 0" class="sp__errors" role="alert" aria-label="Fehler im Suchprofil">
        <li v-for="message in errors" :key="message">
          {{ message }}
        </li>
      </ul>
      <div v-if="rescoring || offerImport" class="sp__after" role="status">
        <span v-if="rescoring">Stellen werden mit dem neuen Suchprofil bewertet …</span>
        <span v-else>Gespeichert. Neue Suchbegriffe oder Orte wirken beim nächsten Import.</span>
        <button v-if="offerImport" type="button" class="button button--small" @click="startImport">
          Jetzt importieren
        </button>
      </div>

      <section class="sp__section" aria-labelledby="sec-search">
        <h2 id="sec-search">
          Suche
        </h2>
        <fieldset class="sp__rows">
          <legend>Suchbegriffe</legend>
          <p class="sp__help">
            Jeder Begriff wird pro Ort und für Remote gesucht; „Nur Region" oder „Nur Remote" schränken das ein.
          </p>
          <div v-for="(term, index) in form.searchTerms" :key="index" class="sp__row">
            <label :for="`term-${index}`" class="visually-hidden">Suchbegriff {{ index + 1 }}</label>
            <input :id="`term-${index}`" v-model.trim="term.text" class="input" placeholder="z. B. .NET Entwickler">
            <label :for="`scope-${index}`" class="visually-hidden">Suchen in</label>
            <select :id="`scope-${index}`" v-model="term.scope" class="input">
              <option v-for="option in SCOPE_OPTIONS" :key="option.value" :value="option.value">
                {{ option.label }}
              </option>
            </select>
            <button type="button" class="button button--secondary button--small" :aria-label="`Suchbegriff ${term.text || index + 1} entfernen`" @click="form.searchTerms.splice(index, 1)">
              Entfernen
            </button>
          </div>
          <button type="button" class="button button--secondary button--small" @click="addTerm">
            Suchbegriff hinzufügen
          </button>
        </fieldset>

        <fieldset class="sp__rows">
          <legend>Orte</legend>
          <div v-for="(place, index) in form.places" :key="index" class="sp__row">
            <label :for="`city-${index}`" class="visually-hidden">Ort {{ index + 1 }}</label>
            <input :id="`city-${index}`" v-model.trim="place.city" class="input" placeholder="z. B. Deggendorf">
            <label :for="`radius-${index}`" class="sp__inline">Umkreis (km)
              <input :id="`radius-${index}`" v-model.number="place.radiusKm" class="input sp__number" type="number" min="0" max="200" step="5">
            </label>
            <button type="button" class="button button--secondary button--small" :aria-label="`Ort ${place.city || index + 1} entfernen`" @click="form.places.splice(index, 1)">
              Entfernen
            </button>
          </div>
          <button type="button" class="button button--secondary button--small" @click="addPlace">
            Ort hinzufügen
          </button>
          <label class="sp__check">
            <input v-model="form.remoteGermany" type="checkbox">
            Remote deutschlandweit suchen
          </label>
        </fieldset>

        <TagInput
          id="additional-places"
          v-model="form.additionalPlaces"
          label="Weitere Orte in meiner Region"
          hint="Zählen für „in meiner Region“, ohne eigene Suche (z. B. Plattling, Straubing)."
        />

        <div class="sp__grid">
          <label for="published">Veröffentlicht in den letzten … Tagen
            <input id="published" v-model.number="form.publishedWithinDays" class="input sp__number" type="number" min="1" max="100">
          </label>
        </div>

        <fieldset>
          <legend>Quellen</legend>
          <label class="sp__check"><input v-model="form.sources.arbeitsagentur" type="checkbox"> Arbeitsagentur</label>
          <label class="sp__check"><input v-model="form.sources.himalayas" type="checkbox"> Himalayas (nur Remote)</label>
        </fieldset>
      </section>

      <section class="sp__section" aria-labelledby="sec-remote">
        <h2 id="sec-remote">
          Remote und Arbeitgeber
        </h2>
        <fieldset>
          <legend>Remote-Präferenz</legend>
          <label v-for="option in REMOTE_OPTIONS" :key="option.value" class="sp__check">
            <input v-model="form.remotePreference" type="radio" name="remote" :value="option.value">
            {{ option.label }} <span class="sp__muted">– {{ option.hint }}</span>
          </label>
        </fieldset>
        <div class="sp__grid">
          <label for="agency">Personalvermittlung
            <select id="agency" v-model="form.agency" class="input">
              <option v-for="option in POLICY_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
          </label>
          <label for="temporary">Zeitarbeit
            <select id="temporary" v-model="form.temporary" class="input">
              <option v-for="option in POLICY_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
          </label>
        </div>
        <p class="sp__help">
          „Ausblenden" blendet sie in der Liste aus, solange dort kein Arbeitgebertyp gewählt ist.
        </p>
      </section>

      <section class="sp__section" aria-labelledby="sec-stack">
        <h2 id="sec-stack">
          Stack
        </h2>
        <p class="sp__help">
          Auf einen Begriff klicken, um Gewicht, Schreibweisen und Optionen zu ändern.
        </p>
        <KeywordList id="must-have" v-model="form.mustHave" kind="mustHave" label="Must-have" hint="Kern meiner Suche; erhöht die Relevanz stark." />
        <KeywordList id="nice-to-have" v-model="form.niceToHave" kind="niceToHave" label="Nice-to-have" hint="Kleiner Bonus." />
        <KeywordList id="exclude" v-model="form.exclude" kind="exclude" label="Ausschluss" hint="Senkt die Relevanz (z. B. Java, SAP)." />
        <TagInput
          id="title-exclusions"
          v-model="form.titleExclusions"
          label="Titel-Ausschlüsse"
          hint="Stellen mit diesen Wörtern im Titel werden beim Import übersprungen (z. B. Praktikum, Werkstudent)."
        />
      </section>

      <section class="sp__section" aria-labelledby="sec-score">
        <h2 id="sec-score">
          Bewertung
        </h2>
        <div class="sp__grid">
          <label for="min-score">Mindestbewertung in der Liste (0–100)
            <input id="min-score" v-model.number="form.minScore" class="input sp__number" type="number" min="0" max="100" step="5">
          </label>
          <label for="top-n">„Nur Top N" zeigt (1–200)
            <input id="top-n" v-model.number="form.topN" class="input sp__number" type="number" min="1" max="200">
          </label>
        </div>
        <fieldset>
          <legend>Standard für „Deutsch erforderlich" in der Liste</legend>
          <p class="sp__help">
            Gilt, solange in der Liste nichts anderes gewählt ist. Keine Auswahl = alle Stellen.
          </p>
          <label v-for="level in REQUIRED_GERMAN" :key="level" class="sp__check">
            <input
              type="checkbox"
              :value="level"
              :checked="form.germanFilter.includes(level)"
              @change="form.germanFilter = toggleListValue(form.germanFilter, level, ($event.target as HTMLInputElement).checked, REQUIRED_GERMAN)"
            >
            {{ requiredGermanLabel(level) }}
          </label>
        </fieldset>
        <label class="sp__check">
          <input v-model="form.hideUnreachable" type="checkbox" data-test="hide-unreachable">
          Nicht erreichbare Stellen ausblenden (außerhalb meiner Orte, nicht voll remote, höchstens 60 % Homeoffice)
        </label>
        <fieldset>
          <legend>Fit-Abzüge (0–{{ MAX_PENALTY }} Punkte)</legend>
          <p class="sp__help">
            Werden von der Relevanz abgezogen, wenn eine Stelle nicht zu mir passt.
          </p>
          <div class="sp__grid">
            <label for="pen-german">je fehlende Deutschstufe
              <input id="pen-german" v-model.number="form.penalties.germanPerLevel" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-exp">zu wenig Erfahrung („Senior" im Titel)
              <input id="pen-exp" v-model.number="form.penalties.experience" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-exp-strong">zu wenig Erfahrung, deutlich gefordert („langjährig", „5+ Jahre", „Lead")
              <input id="pen-exp-strong" v-model.number="form.penalties.experienceStrong" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-must">je fehlende Muss-Anforderung („Was du mitbringst")
              <input id="pen-must" v-model.number="form.penalties.mustHaveGap" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-gap">je fehlende sonstige Pflicht-Technologie
              <input id="pen-gap" v-model.number="form.penalties.requiredGap" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-stack">je fehlende Technologie im Tech-Stack („Sprachen &amp; Technologien")
              <input id="pen-stack" v-model.number="form.penalties.likelyRequiredGap" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-plus">je fehlendes „Plus" („von Vorteil", „kein Muss")
              <input id="pen-plus" v-model.number="form.penalties.niceToHaveGap" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-must-max">höchstens für fehlende Muss-Anforderungen
              <input id="pen-must-max" v-model.number="form.penalties.mustHaveGapMax" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-gap-max">höchstens für alle anderen fehlenden Technologien
              <input id="pen-gap-max" v-model.number="form.penalties.requiredGapMax" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-region">außerhalb der Region
              <input id="pen-region" v-model.number="form.penalties.outsideRegion" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-unreachable">praktisch nicht erreichbar (höchstens 60 % Homeoffice)
              <input id="pen-unreachable" v-model.number="form.penalties.unreachable" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-employer">„niedriger einordnen"
              <input id="pen-employer" v-model.number="form.penalties.employerType" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
            <label for="pen-remote">Remote-Wunsch verfehlt
              <input id="pen-remote" v-model.number="form.penalties.remotePreference" class="input sp__number" type="number" min="0" :max="MAX_PENALTY">
            </label>
          </div>
        </fieldset>
      </section>

      <section class="sp__section" aria-labelledby="sec-terms">
        <h2 id="sec-terms">
          Rahmen
        </h2>
        <p class="sp__help">
          Nur zur Orientierung (und später für Anschreiben); fließt noch nicht in die Bewertung ein.
        </p>
        <div class="sp__grid">
          <label for="salary-min">Gehalt ab (€ brutto/Jahr)
            <input id="salary-min" class="input sp__number" type="number" min="0" step="1000" :value="form.salaryMin ?? ''" @input="form.salaryMin = numberOrNull(($event.target as HTMLInputElement).value)">
          </label>
          <label for="salary-max">Gehalt Ziel (€ brutto/Jahr)
            <input id="salary-max" class="input sp__number" type="number" min="0" step="1000" :value="form.salaryMax ?? ''" @input="form.salaryMax = numberOrNull(($event.target as HTMLInputElement).value)">
          </label>
          <label for="start">Frühester Start
            <input id="start" class="input" type="date" :value="form.earliestStart ?? ''" @input="form.earliestStart = ($event.target as HTMLInputElement).value || null">
          </label>
        </div>
      </section>

      <section class="sp__section" aria-labelledby="sec-searches">
        <h2 id="sec-searches">
          Daraus erzeugte Import-Suchen ({{ searches.length }})
        </h2>
        <p class="sp__help">
          Nur zur Ansicht; ändern über Suchbegriffe, Orte und Quellen.
        </p>
        <ul class="sp__searches">
          <li v-for="search in searches" :key="search.name">
            <code>{{ search.name }}</code> {{ searchLabel(search) }}
          </li>
        </ul>
      </section>

      <section class="sp__section" aria-labelledby="sec-history">
        <h2 id="sec-history">
          Verlauf und Datei
        </h2>
        <div class="sp__actions">
          <a class="button button--secondary button--small" :href="api.searchProfileExportUrl()" download>Als JSON exportieren</a>
          <label class="button button--secondary button--small sp__file" for="import-file">JSON importieren</label>
          <input id="import-file" class="visually-hidden" type="file" accept="application/json,.json" @change="onImportFile">
        </div>
        <p v-if="importMessage" class="sp__help" role="status">
          {{ importMessage }}
        </p>
        <ol v-if="history.length > 0" class="sp__history">
          <li v-for="revision in history" :key="revision.version">
            <span><strong>Version {{ revision.version }}</strong> · {{ formatDateTime(revision.savedAt) }}</span>
            <span class="sp__muted">{{ revision.summary }}</span>
            <span v-if="revision.version === loaded?.version" class="sp__muted">aktuell</span>
            <button v-else type="button" class="button button--secondary button--small" @click="rollback(revision.version)">
              Wiederherstellen
            </button>
          </li>
        </ol>
        <p v-else class="sp__help">
          Noch keine gespeicherten Versionen.
        </p>
      </section>
    </form>
  </main>
</template>

<style scoped lang="scss">
.sp {
  display: grid;
  gap: var(--space-4);
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-4);

  &__header {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: var(--space-3);

    h1 {
      margin: 0;
    }
  }

  &__version,
  &__muted,
  &__hint,
  &__help {
    color: var(--color-muted);
  }

  &__hint,
  &__help {
    margin: 0;
  }

  &__help {
    font-size: 0.85rem;
  }

  &__form {
    display: grid;
    gap: var(--space-4);
  }

  &__bar {
    position: sticky;
    top: 0;
    z-index: 2;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-3);
    padding: var(--space-3);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
    background: var(--color-surface);
  }

  &__preview {
    margin: 0;
    font-weight: 600;
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-2);
  }

  &__errors {
    margin: 0;
    padding: var(--space-3) var(--space-3) var(--space-3) var(--space-5);
    border-radius: var(--radius);
    background: var(--color-warning-bg);
    color: var(--color-error-text);
  }

  &__after {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-3);
  }

  &__section {
    display: grid;
    gap: var(--space-3);
    padding: var(--space-4);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
    background: var(--color-surface);

    h2 {
      margin: 0;
      font-size: 1.1rem;
    }

    fieldset {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      border: 0;
    }

    legend {
      margin-bottom: var(--space-1);
      font-size: 0.85rem;
      font-weight: 600;
    }
  }

  &__rows {
    justify-items: start;
  }

  &__row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-2);
    width: 100%;

    > .input:first-of-type {
      flex: 1 1 14rem;
    }
  }

  &__grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
    gap: var(--space-3);

    label {
      display: grid;
      gap: var(--space-1);
      font-size: 0.85rem;
    }
  }

  &__inline {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    font-size: 0.85rem;
  }

  &__number {
    max-width: 8rem;
  }

  &__check {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
  }

  &__file {
    cursor: pointer;
  }

  &__searches,
  &__history {
    display: grid;
    gap: var(--space-2);
    margin: 0;
    padding-left: var(--space-4);
    font-size: 0.9rem;
  }

  &__history li {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-1) var(--space-3);
  }
}
</style>
