<script setup lang="ts">
import type { ApplicationDocuments, CvOptions, DocumentKind, Letter } from '~/types/documents'

const PREVIEW_DELAY_MS = 700

const route = useRoute()
const api = useVacanciesApi()
const toasts = useToasts()
const applicationId = computed(() => String(route.params.id))

const documents = ref<ApplicationDocuments | null>(null)
const letter = ref<Letter | null>(null)
const cvOptions = ref<CvOptions | null>(null)
const loadError = ref<string | null>(null)
const missing = ref(false)
const busy = ref<'create' | 'save' | 'regenerate' | 'approve' | 'send' | 'restore' | null>(null)
const rephrasing = ref<number | null>(null)
const conflict = ref(false)
const acceptWarnings = ref(false)
const channel = ref(SEND_CHANNELS[0]!)
const sentOn = ref(new Date().toISOString().slice(0, 10))
const previewKind = ref<DocumentKind>('letter')
const preview = ref<{ url: string, pages: number } | null>(null)
const previewPages = ref<Record<DocumentKind, number | null>>({ letter: null, cv: null })
const previewError = ref(false)
let previewAbort: AbortController | null = null

useHead(() => ({ title: documents.value ? `Unterlagen ${documents.value.company} · Job Autopilot` : 'Unterlagen · Job Autopilot' }))

const editable = computed(() => documents.value != null && canEditDocuments(documents.value.status))
const dirty = computed(() => letter.value != null && cvOptions.value != null && documents.value != null
  && isDocumentsDirty(letter.value, cvOptions.value, documents.value))
const blocking = computed(() => documents.value?.hasOpenWarnings ?? false)
const canApprove = computed(() => editable.value && !dirty.value && busy.value === null && (!blocking.value || acceptWarnings.value))

function apply(result: ApplicationDocuments) {
  documents.value = result
  letter.value = cloneLetter(result.letter)
  cvOptions.value = cloneCvOptions(result.cvOptions)
  previewPages.value = { letter: result.letterPages, cv: result.cvPages }
  conflict.value = false
  acceptWarnings.value = false
}

async function load() {
  loadError.value = null
  missing.value = false
  try {
    apply(await api.documents(applicationId.value))
  }
  catch (error: unknown) {
    if (errorStatus(error) === 404) {
      missing.value = true
    }
    else {
      loadError.value = describeApiError(error)
    }
  }
}

/** Runs one action; 409 shows the reload hint instead of a toast. */
async function run(kind: NonNullable<typeof busy.value>, action: () => Promise<ApplicationDocuments>, success?: string) {
  busy.value = kind
  try {
    apply(await action())
    if (success) {
      toasts.show(success)
    }
    return true
  }
  catch (error: unknown) {
    if (errorStatus(error) === 409 && kind !== 'approve' && kind !== 'send') {
      conflict.value = true
    }
    else {
      toasts.error(describeApiError(error))
    }
    return false
  }
  finally {
    busy.value = null
  }
}

async function create() {
  if (await run('create', () => api.createDocuments(applicationId.value), 'Unterlagen erstellt.')) {
    missing.value = false
  }
}

function save() {
  if (!documents.value || !letter.value || !cvOptions.value) {
    return Promise.resolve(false)
  }
  const { version } = documents.value
  return run('save', () => api.saveDocuments(applicationId.value, version, letter.value!, cvOptions.value!), 'Gespeichert.')
}

async function regenerate() {
  if (!documents.value || (dirty.value && !window.confirm('Ungespeicherte Änderungen gehen verloren. Anschreiben neu erstellen?'))) {
    return
  }
  const { version } = documents.value
  await run('regenerate', () => api.regenerateLetter(applicationId.value, version), 'Anschreiben neu erstellt.')
}

/** Unsaved edits are saved first, so the model rewrites exactly what I see. */
async function rephrase(index: number) {
  if (!documents.value || (dirty.value && !(await save()))) {
    return
  }
  rephrasing.value = index
  try {
    apply(await api.rephraseParagraph(applicationId.value, documents.value.version, index))
    toasts.show(`Absatz ${index + 1} neu formuliert.`)
  }
  catch (error: unknown) {
    toasts.error(describeApiError(error))
  }
  finally {
    rephrasing.value = null
  }
}

function approve() {
  if (!documents.value) {
    return
  }
  const { version } = documents.value
  return run('approve', () => api.approveDocuments(applicationId.value, version, acceptWarnings.value), 'Freigegeben – die PDFs liegen bereit.')
}

function markSent() {
  if (!documents.value || !window.confirm(`Als gesendet über „${channel.value}“ markieren? Das ist nur ein Vermerk – das Tool sendet nichts.`)) {
    return
  }
  const at = new Date(`${sentOn.value}T12:00:00`).toISOString()
  return run('send', () => api.markSent(applicationId.value, channel.value, at), 'Als gesendet markiert.')
}

function restore(revision: number) {
  if (!documents.value || (dirty.value && !window.confirm('Ungespeicherte Änderungen gehen verloren. Trotzdem wiederherstellen?'))) {
    return
  }
  const { version } = documents.value
  return run('restore', () => api.restoreDocuments(applicationId.value, version, revision), `Version ${revision} wiederhergestellt.`)
}

const refreshPreview = debounce(async () => {
  if (!letter.value || !cvOptions.value) {
    return
  }
  previewAbort?.abort()
  previewAbort = new AbortController()
  const kind = previewKind.value
  try {
    const result = await api.previewDocument(applicationId.value, kind, letter.value, cvOptions.value, previewAbort.signal)
    if (preview.value) {
      URL.revokeObjectURL(preview.value.url)
    }
    preview.value = result
    previewPages.value = { ...previewPages.value, [kind]: result.pages }
    previewError.value = false
  }
  catch (error: unknown) {
    if (!(error instanceof DOMException && error.name === 'AbortError')) {
      previewError.value = true
    }
  }
}, PREVIEW_DELAY_MS)

watch([letter, cvOptions, previewKind], () => refreshPreview(), { deep: true })

const LEAVE_QUESTION = 'Es gibt ungespeicherte Änderungen an den Unterlagen. Seite trotzdem verlassen?'
onBeforeRouteLeave(() => !dirty.value || window.confirm(LEAVE_QUESTION))

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
  refreshPreview.cancel()
  previewAbort?.abort()
  if (preview.value) {
    URL.revokeObjectURL(preview.value.url)
  }
})
</script>

<template>
  <main class="docs">
    <NuxtLink to="/bewerbungen" class="docs__back">
      ← Bewerbungen
    </NuxtLink>

    <div v-if="loadError" class="state state--error" role="alert">
      Die Unterlagen konnten nicht geladen werden. {{ loadError }}
      <button type="button" class="button button--secondary button--small" @click="load">
        Erneut versuchen
      </button>
    </div>

    <div v-else-if="missing" class="state">
      <p>Für diese Bewerbung gibt es noch keine Unterlagen.</p>
      <button type="button" class="button" :disabled="busy !== null" :aria-busy="busy === 'create'" @click="create">
        {{ busy === 'create' ? 'Unterlagen werden erstellt …' : 'Unterlagen erstellen' }}
      </button>
    </div>

    <div v-else-if="!documents || !letter || !cvOptions" class="state" role="status">
      Unterlagen werden geladen …
    </div>

    <template v-else>
      <header class="docs__header">
        <div>
          <h1>{{ documents.position }}</h1>
          <p class="docs__company">
            {{ documents.company }}
            <span class="status" :class="`status--${statusTone(documents.status)}`">{{ documents.statusLabel }}</span>
            <NuxtLink v-if="documents.vacancyId" :to="`/vacancies/${documents.vacancyId}`">Stelle ansehen</NuxtLink>
          </p>
        </div>
        <p class="docs__usage">
          {{ documents.model }} · {{ documents.promptVersion }} ·
          {{ usageLabel(documents.usage.inputTokens, documents.usage.outputTokens, documents.usage.costUsd) }}
          · heute {{ documents.usage.callsToday }}/{{ documents.usage.dailyCallLimit }} Aufrufe
        </p>
      </header>

      <div v-if="conflict" class="state state--error" role="alert">
        Die Unterlagen wurden inzwischen an anderer Stelle geändert. Neu laden verwirft die Änderungen hier.
        <button type="button" class="button button--secondary button--small" @click="load">
          Neu laden
        </button>
      </div>
      <p v-if="!documents.claudeConfigured" class="docs__note">
        Kein Claude-API-Schlüssel konfiguriert – Anschreiben können bearbeitet, aber nicht neu erstellt werden.
      </p>

      <div class="docs__bar" role="region" aria-label="Aktionen">
        <button type="button" class="button" :disabled="!editable || !dirty || busy !== null" :aria-busy="busy === 'save'" @click="save">
          {{ busy === 'save' ? 'Wird gespeichert …' : 'Speichern' }}
        </button>
        <button type="button" class="button button--secondary" :disabled="!editable || busy !== null" :aria-busy="busy === 'regenerate'" @click="regenerate">
          {{ busy === 'regenerate' ? 'Wird erstellt …' : 'Anschreiben neu erstellen' }}
        </button>
        <span v-if="dirty" class="docs__dirty">Ungespeicherte Änderungen</span>
      </div>

      <div class="docs__grid">
        <div class="docs__column">
          <LetterEditor
            v-model="letter"
            :warnings="documents.warnings"
            :disabled="!editable || busy !== null"
            :rephrasing="rephrasing"
            :rendered-pages="dirty ? previewPages.letter : documents.letterPages"
            @rephrase="rephrase"
          />
          <CvOptionsEditor v-model="cvOptions" :cv="documents.cv" :disabled="!editable || busy !== null" />
        </div>

        <div class="docs__column">
          <DraftWarnings :warnings="documents.warnings" />

          <section class="docs__preview" aria-labelledby="preview-heading">
            <h2 id="preview-heading">
              Vorschau
            </h2>
            <div class="docs__tabs" role="group" aria-label="Dokument">
              <button type="button" class="button button--small" :class="{ 'button--secondary': previewKind !== 'letter' }" :aria-pressed="previewKind === 'letter'" @click="previewKind = 'letter'">
                Anschreiben
              </button>
              <button type="button" class="button button--small" :class="{ 'button--secondary': previewKind !== 'cv' }" :aria-pressed="previewKind === 'cv'" @click="previewKind = 'cv'">
                Lebenslauf
              </button>
              <span class="docs__pages" aria-live="polite">
                {{ previewPages[previewKind] ?? '–' }} {{ previewPages[previewKind] === 1 ? 'Seite' : 'Seiten' }}
              </span>
            </div>
            <img
              v-if="preview"
              :src="preview.url"
              :alt="previewKind === 'letter' ? 'Vorschau des Anschreibens, Seite 1' : 'Vorschau des Lebenslaufs, Seite 1'"
              class="docs__image"
            >
            <p v-else-if="previewError" class="docs__note" role="status">
              Vorschau nicht verfügbar.
            </p>
            <p v-else class="docs__note" role="status">
              Vorschau wird erstellt …
            </p>
          </section>

          <section class="docs__approve" aria-labelledby="approve-heading">
            <h2 id="approve-heading">
              Freigeben und absenden
            </h2>
            <template v-if="editable">
              <p v-if="dirty" class="docs__note">
                Erst speichern, dann freigeben.
              </p>
              <label v-if="blocking" class="docs__check">
                <input v-model="acceptWarnings" type="checkbox">
                Ich habe die Warnungen geprüft und gebe trotzdem frei.
              </label>
              <button type="button" class="button" :disabled="!canApprove" :aria-busy="busy === 'approve'" @click="approve">
                {{ documents.status === 'Approved' ? 'Erneut freigeben' : 'Freigeben' }}
              </button>
            </template>

            <div v-if="documents.approvedAt" class="docs__files">
              <p>
                Freigegeben {{ formatDateTime(documents.approvedAt) }}<template v-if="documents.warningsAccepted">
                  (mit bestätigten Warnungen)
                </template>
              </p>
              <a class="button button--secondary button--small" :href="api.documentPdfUrl(documents.applicationId, 'letter')" download>
                {{ documents.letterFile }} herunterladen
              </a>
              <a class="button button--secondary button--small" :href="api.documentPdfUrl(documents.applicationId, 'cv')" download>
                {{ documents.cvFile }} herunterladen
              </a>
            </div>

            <form v-if="documents.status === 'Approved' && documents.approvedAt" class="docs__send" @submit.prevent="markSent">
              <p class="docs__note">
                Das Tool sendet nichts. Nach dem Absenden über Portal, Formular oder E-Mail hier vermerken:
              </p>
              <label for="send-channel">Weg</label>
              <select id="send-channel" v-model="channel" class="input">
                <option v-for="option in SEND_CHANNELS" :key="option" :value="option">
                  {{ option }}
                </option>
              </select>
              <label for="send-date">Gesendet am</label>
              <input id="send-date" v-model="sentOn" class="input" type="date" required>
              <button type="submit" class="button" :disabled="busy !== null" :aria-busy="busy === 'send'">
                Als gesendet markieren
              </button>
            </form>
          </section>

          <ApplicationCheatSheet :items="documents.cheatSheet" />

          <section aria-labelledby="history-heading" class="docs__history">
            <h2 id="history-heading">
              Verlauf
            </h2>
            <ol>
              <li v-for="revision in documents.revisions" :key="revision.number">
                <span>
                  <strong>Version {{ revision.number }}</strong> · {{ revisionLabel(revision.kind) }}<template v-if="revision.note">
                    ({{ revision.note }})
                  </template>
                  · {{ formatDateTime(revision.createdAt) }}
                </span>
                <span v-if="revision.inputTokens > 0" class="docs__muted">{{ (revision.inputTokens + revision.outputTokens).toLocaleString('de-DE') }} Tokens</span>
                <button
                  v-if="editable && revision.number !== documents.revisions[0]?.number"
                  type="button"
                  class="button button--secondary button--small"
                  :disabled="busy !== null"
                  @click="restore(revision.number)"
                >
                  Wiederherstellen
                </button>
              </li>
            </ol>
          </section>
        </div>
      </div>
    </template>
  </main>
</template>

<style scoped lang="scss">
.docs {
  display: grid;
  gap: var(--space-4);
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-4);

  h1 {
    margin: 0;
    font-size: 1.4rem;
  }

  h2 {
    margin: 0;
    font-size: 1.1rem;
  }

  &__header {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-end;
    justify-content: space-between;
    gap: var(--space-3);
  }

  &__company {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-2) var(--space-3);
    margin: var(--space-1) 0 0;
    color: var(--color-muted);
  }

  &__usage,
  &__note,
  &__muted,
  &__pages {
    margin: 0;
    font-size: 0.85rem;
    color: var(--color-muted);
  }

  &__bar {
    position: sticky;
    top: 0;
    z-index: 2;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-3);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
    background: var(--color-surface);
  }

  &__dirty {
    font-weight: 600;
    color: var(--color-warning-text);
  }

  &__grid {
    display: grid;
    grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr);
    gap: var(--space-4);
    align-items: start;

    @media (max-width: 960px) {
      grid-template-columns: minmax(0, 1fr);
    }
  }

  &__column {
    display: grid;
    gap: var(--space-4);

    > section {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
    }
  }

  &__preview,
  &__approve,
  &__files,
  &__send,
  &__history {
    display: grid;
    gap: var(--space-2);
    justify-items: start;
  }

  &__tabs {
    display: flex;
    align-items: center;
    gap: var(--space-2);
  }

  &__image {
    width: 100%;
    border: 1px solid var(--color-border);
    box-shadow: 0 1px 4px rgb(0 0 0 / 10%);
    background: #fff;
  }

  &__check {
    display: inline-flex;
    align-items: center;
    gap: var(--space-2);
    font-weight: 600;
    color: var(--color-warning-text);
  }

  &__send label {
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__history ol {
    display: grid;
    gap: var(--space-2);
    margin: 0;
    padding-left: var(--space-4);
    font-size: 0.9rem;

    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-1) var(--space-3);
    }
  }
}
</style>
