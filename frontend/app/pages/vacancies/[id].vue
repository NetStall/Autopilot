<script setup lang="ts">
import type { Application } from '~/types/application'
import type { VacancyInterest } from '~/types/vacancy'

const route = useRoute()
const api = useVacanciesApi()

const id = computed(() => String(route.params.id))
/** The list filters travel along in the query so the back link restores them. */
const backTo = computed(() => ({ path: '/', query: route.query }))

const { data: vacancy, status, error, refresh } = useAsyncData(
  () => `vacancy-${id.value}`,
  () => api.get(id.value),
  { server: false, lazy: true },
)

useHead({ title: () => (vacancy.value ? `${vacancy.value.title} · Job Autopilot` : 'Stelle · Job Autopilot') })

const originalUrl = computed(() => safeExternalUrl(vacancy.value?.url))
const badges = computed(() => (vacancy.value ? vacancyBadges(vacancy.value) : []))
const statusBadges = computed(() => badges.value.filter(b => b.variant === 'new' || b.variant === 'remote' || b.variant === 'hybrid'))
const thresholds = useScoreThresholds()
const score = computed(() => (vacancy.value ? scoreBadge(vacancy.value, thresholds.value) : null))
const { data: fit, refresh: refreshFit } = useAsyncData(() => `vacancy-fit-${id.value}`, () => api.fit(id.value), { server: false, lazy: true })

/** After „Mit KI analysieren": the vacancy (extraction, rank) and its fit check come back from the server. */
async function reloadAnalysis() {
  await Promise.all([refresh(), refreshFit()])
}
const attribution = computed(() => (vacancy.value ? sourceAttribution(vacancy.value.source) : null))
const employerBadge = computed(() => badges.value.find(b => b.variant === 'employer' || b.variant === 'intermediary'))
const notFound = computed(() => errorStatus(error.value) === 404)
const saving = ref(false)
const applying = ref(false)

async function markApplied() {
  if (!vacancy.value) {
    return
  }
  applying.value = true
  actionError.value = null
  try {
    const application = await api.markApplied(vacancy.value.id)
    vacancy.value = {
      ...vacancy.value,
      applied: { applicationId: application.id, status: application.status, statusLabel: application.statusLabel, appliedAt: application.appliedAt },
    }
  }
  catch (e: unknown) {
    actionError.value = `Konnte nicht als beworben markiert werden: ${describeApiError(e)}`
  }
  finally {
    applying.value = false
  }
}
const actionError = ref<string | null>(null)

/** „Status" changed on this page (08a): Absage, Interview … */
function statusChanged(application: Application) {
  if (vacancy.value) {
    vacancy.value = {
      ...vacancy.value,
      applied: { applicationId: application.id, status: application.status, statusLabel: application.statusLabel, appliedAt: application.appliedAt },
    }
  }
}

/** A draft application of this vacancy whose documents are still being prepared. */
const draftApplicationId = computed(() => {
  const applied = vacancy.value?.applied
  return applied && (applied.status === 'Draft' || applied.status === 'Approved') ? applied.applicationId : null
})
const creatingDocuments = ref(false)

/** „Unterlagen erstellen": one Claude call for the letter, the CV is built without AI; then the review screen. */
async function createDocuments() {
  if (!vacancy.value) {
    return
  }
  creatingDocuments.value = true
  actionError.value = null
  try {
    const documents = await api.createDocumentsForVacancy(vacancy.value.id)
    await navigateTo(`/unterlagen/${documents.applicationId}`)
  }
  catch (e: unknown) {
    actionError.value = `Unterlagen konnten nicht erstellt werden: ${describeApiError(e)}`
  }
  finally {
    creatingDocuments.value = false
  }
}

async function setSeen(isSeen: boolean) {
  if (!vacancy.value) {
    return
  }
  saving.value = true
  actionError.value = null
  try {
    await api.setSeen(vacancy.value.id, isSeen)
    vacancy.value = { ...vacancy.value, isSeen }
  }
  catch (e: unknown) {
    actionError.value = `Status konnte nicht gespeichert werden: ${describeApiError(e)}`
  }
  finally {
    saving.value = false
  }
}

async function setInterest(interest: VacancyInterest) {
  if (!vacancy.value) {
    return
  }
  saving.value = true
  actionError.value = null
  try {
    await api.setInterest(vacancy.value.id, interest)
    vacancy.value = { ...vacancy.value, interest }
  }
  catch (e: unknown) {
    actionError.value = `Änderung konnte nicht gespeichert werden: ${describeApiError(e)}`
  }
  finally {
    saving.value = false
  }
}

const interestLabels: Record<VacancyInterest, string> = { None: '–', Starred: '★ Gemerkt', Dismissed: 'Ausgeblendet' }

function openedOriginal() {
  if (vacancy.value && !vacancy.value.isSeen) {
    void setSeen(true)
  }
}
</script>

<template>
  <main class="detail">
    <NuxtLink :to="backTo" class="detail__back">
      ← Zurück zur Liste
    </NuxtLink>

    <div v-if="status === 'idle' || status === 'pending'" class="state" role="status">
      Stelle wird geladen …
    </div>

    <div v-else-if="notFound" class="state">
      <p><strong>Diese Stelle gibt es nicht (mehr).</strong></p>
      <NuxtLink :to="backTo">
        Zur Liste
      </NuxtLink>
    </div>

    <div v-else-if="error" class="state state--error" role="alert">
      <p>Die Stelle konnte nicht geladen werden. {{ describeApiError(error) }}</p>
      <button type="button" class="button" @click="refresh()">
        Erneut versuchen
      </button>
    </div>

    <article v-else-if="vacancy" class="detail__card">
      <header class="detail__header">
        <div>
          <span
            v-for="badge in statusBadges"
            :key="badge.variant"
            class="badge detail__badge"
            :class="`badge--${badge.variant}`"
          >{{ badge.label }}</span>
          <ScoreBadge v-if="vacancy" :vacancy="vacancy" show-detail class="detail__badge" />
          <h1>{{ vacancy.title }}</h1>
          <p class="detail__company">
            {{ vacancy.company }}<span v-if="vacancy.location"> · {{ vacancy.location }}</span>
          </p>
        </div>
        <span
          v-if="employerBadge"
          class="badge"
          :class="{ 'badge--intermediary': employerBadge.variant === 'intermediary' }"
        >{{ employerBadge.label }}</span>
      </header>

      <div class="detail__actions">
        <a
          v-if="originalUrl"
          class="button"
          :href="originalUrl"
          target="_blank"
          rel="noopener noreferrer"
          @click="openedOriginal"
          @auxclick="openedOriginal"
        >
          Originalanzeige öffnen<span class="visually-hidden"> (neuer Tab)</span>
        </a>
        <button
          v-if="!vacancy.applied"
          type="button"
          class="button"
          :disabled="creatingDocuments"
          :aria-busy="creatingDocuments"
          @click="createDocuments"
        >
          {{ creatingDocuments ? 'Unterlagen werden erstellt (bis zu 1 Minute) …' : 'Unterlagen erstellen' }}
        </button>
        <NuxtLink v-else-if="draftApplicationId" :to="`/unterlagen/${draftApplicationId}`" class="button">
          Unterlagen öffnen · {{ vacancy.applied.statusLabel }}
        </NuxtLink>
        <button v-if="!vacancy.applied" type="button" class="button button--secondary" :disabled="applying" @click="markApplied">
          Als beworben markieren
        </button>
        <NuxtLink v-else-if="!draftApplicationId" to="/bewerbungen" class="status status--open detail__applied">
          Beworben am {{ formatDate(vacancy.applied.appliedAt) }} · {{ vacancy.applied.statusLabel }}
        </NuxtLink>
        <ApplicationStatusSelect
          v-if="vacancy.applied"
          :application-id="vacancy.applied.applicationId"
          :status="vacancy.applied.status"
          @changed="statusChanged"
        />
        <button type="button" class="button button--secondary" :disabled="saving" @click="setSeen(!vacancy.isSeen)">
          {{ vacancy.isSeen ? 'Als neu markieren' : 'Als gesehen markieren' }}
        </button>
        <button
          type="button"
          class="button button--secondary"
          :disabled="saving"
          :aria-pressed="vacancy.interest === 'Starred'"
          @click="setInterest(vacancy.interest === 'Starred' ? 'None' : 'Starred')"
        >
          {{ vacancy.interest === 'Starred' ? '★ Gemerkt' : '☆ Merken' }}
        </button>
        <button
          type="button"
          class="button button--secondary"
          :disabled="saving"
          :aria-pressed="vacancy.interest === 'Dismissed'"
          @click="setInterest(vacancy.interest === 'Dismissed' ? 'None' : 'Dismissed')"
        >
          {{ vacancy.interest === 'Dismissed' ? 'Einblenden' : 'Ausblenden' }}
        </button>
      </div>
      <p v-if="actionError" class="detail__error" role="alert">
        {{ actionError }}
      </p>

      <section class="detail__reasons" aria-labelledby="label-heading">
        <h2 id="label-heading">
          Meine Bewertung
        </h2>
        <GoldLabelButtons v-model="vacancy.goldLabel" v-model:automatic="vacancy.goldLabelAutomatic" :vacancy-id="vacancy.id" />
      </section>

      <p v-if="vacancy.howToApply" class="detail__how" :class="{ 'detail__how--portal': vacancy.howToApply.portalOnly }">
        <strong>Bewerbung:</strong> {{ howToApplyLine(vacancy.howToApply) }}
        <span class="detail__how-source">· Quelle: {{ SOURCE_LABELS[vacancy.howToApply.source] }}</span>
        <template v-if="vacancy.howToApply.requested.length">
          · gewünscht: {{ vacancy.howToApply.requested.join(', ') }}
        </template>
      </p>

      <section class="detail__reasons" aria-labelledby="requirements-heading">
        <h2 id="requirements-heading">
          Anforderungen
        </h2>
        <VacancyRequirements
          :vacancy-id="vacancy.id"
          :extraction="vacancy.extraction"
          :source="fit?.source"
          :has-description="!!vacancy.description"
          @analysed="reloadAnalysis"
        />
      </section>

      <section v-if="fit" class="detail__reasons" aria-labelledby="fit-heading">
        <h2 id="fit-heading">
          Passt zu mir?
        </h2>
        <VacancyFit :fit="fit" />
      </section>

      <section class="detail__reasons" aria-labelledby="reasons-heading">
        <h2 id="reasons-heading">
          Warum diese Bewertung?
        </h2>
        <p v-if="scoreLine(vacancy.relevanceScore, vacancy.rankScore)" class="detail__score-line">
          {{ scoreLine(vacancy.relevanceScore, vacancy.rankScore, vacancy.aiScore) }}
        </p>
        <VacancyReasons :reasons="vacancy.relevanceReasons" />
        <AiFitScorePanel v-if="vacancy.aiFitScore" :score="vacancy.aiFitScore" />
      </section>

      <dl class="detail__facts">
        <dt>Arbeitgeber</dt>
        <dd>{{ vacancy.company }}</dd>
        <dt>Ort</dt>
        <dd>{{ vacancy.location ?? '–' }}</dd>
        <dt>Art des Arbeitgebers</dt>
        <dd>{{ employerTypeLabel(vacancy.employerType) }}</dd>
        <dt>Veröffentlicht</dt>
        <dd>
          <template v-if="vacancy.publishedAt">
            <time :datetime="vacancy.publishedAt">{{ formatDate(vacancy.publishedAt) }}</time>
            ({{ formatRelativeDate(vacancy.publishedAt) }})
          </template>
          <template v-else>
            –
          </template>
        </dd>
        <dt>Importiert</dt>
        <dd><time :datetime="vacancy.fetchedAt">{{ formatDateTime(vacancy.fetchedAt) }}</time></dd>
        <dt>Quelle</dt>
        <dd>
          <a v-if="attribution" :href="attribution.href" target="_blank" rel="noopener noreferrer">
            {{ sourceLabel(vacancy.source) }}<span class="visually-hidden"> (neuer Tab)</span>
          </a>
          <template v-else>
            {{ sourceLabel(vacancy.source) }}
          </template>
        </dd>
        <template v-if="vacancy.alsoFoundAt.length > 0">
          <dt>Auch gefunden bei</dt>
          <dd>
            <ul class="detail__searches">
              <li v-for="link in vacancy.alsoFoundAt" :key="link.id">
                <NuxtLink :to="{ path: `/vacancies/${link.id}`, query: route.query }">
                  {{ sourceLabel(link.source) }}
                </NuxtLink>
              </li>
            </ul>
          </dd>
        </template>
        <dt>Referenznummer</dt>
        <dd class="detail__mono">
          {{ vacancy.externalId }}
        </dd>
        <dt>Remote</dt>
        <dd>{{ vacancy.remoteNote ?? remoteLevelLabel(vacancy.remoteLevel) }}</dd>
        <dt>Deutsch erforderlich</dt>
        <dd>{{ requiredGermanLabel(vacancy.requiredGerman ?? 'Unknown') }}</dd>
        <dt>Relevanz</dt>
        <dd>{{ score ? `${score.description} (${score.detail})` : '–' }}</dd>
        <dt>Gefunden durch</dt>
        <dd>
          <ul v-if="vacancy.matchedSearches.length > 0" class="detail__searches">
            <li v-for="name in vacancy.matchedSearches" :key="name">
              <NuxtLink :to="{ path: '/', query: { search: name } }">
                {{ name }}
              </NuxtLink>
            </li>
          </ul>
          <template v-else>
            –
          </template>
        </dd>
        <dt>Status</dt>
        <dd>{{ vacancy.isSeen ? 'Gesehen' : 'Neu' }}</dd>
        <dt>Einschätzung</dt>
        <dd>{{ interestLabels[vacancy.interest] ?? '–' }}</dd>
      </dl>

      <section class="detail__description" aria-labelledby="description-heading">
        <h2 id="description-heading">
          Beschreibung
        </h2>
        <p v-if="vacancy.description" class="detail__text">
          {{ vacancy.description }}
        </p>
        <p v-if="vacancy.description && vacancy.descriptionIsManual" class="detail__note">
          Von Hand eingefügt.
        </p>
        <template v-if="!vacancy.description">
          <p v-if="vacancy.source === 'Arbeitsagentur'" class="detail__note">
            {{ vacancy.detailsFetchedAt
              ? 'Externe Anzeige – keine Beschreibung verfügbar. Den Text findest du in der Originalanzeige.'
              : 'Die Beschreibung wird beim nächsten Import geladen. Den vollständigen Text findest du in der Originalanzeige.' }}
          </p>
          <p v-else class="detail__note">
            Keine Beschreibung hinterlegt.
          </p>
          <DescriptionPaste :vacancy-id="vacancy.id" @saved="v => (vacancy = v)" />
        </template>
      </section>
    </article>
  </main>
</template>

<style scoped lang="scss">
.detail {
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);

  &__back {
    display: inline-block;
    margin-bottom: var(--space-4);
  }

  &__card {
    padding: var(--space-5);
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
  }

  &__header {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    gap: var(--space-4);

    h1 {
      margin: var(--space-1) 0;
      font-size: 1.5rem;
      overflow-wrap: anywhere;
    }
  }

  &__badge + &__badge {
    margin-left: var(--space-1);
  }

  &__searches {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-1) var(--space-3);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  &__company {
    margin: 0;
    color: var(--color-muted);
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
    margin: var(--space-4) 0;
  }

  &__error {
    color: var(--color-error-text);
  }

  &__facts {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    gap: var(--space-2) var(--space-4);
    margin: 0;
    padding-top: var(--space-4);
    border-top: 1px solid var(--color-border);

    dt {
      color: var(--color-muted);
    }

    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
  }

  &__mono {
    font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
    font-size: 0.9rem;
  }

  &__reasons,
  &__description {
    margin-top: var(--space-5);
    padding-top: var(--space-4);
    border-top: 1px solid var(--color-border);

    h2 {
      margin: 0 0 var(--space-2);
      font-size: 1.1rem;
    }
  }

  &__text {
    white-space: pre-line;
  }

  &__how {
    margin: var(--space-3) 0;

    &--portal {
      color: var(--color-warning-text);
    }
  }

  &__how-source {
    color: var(--color-muted);
    font-size: 0.9rem;
  }

  &__applied {
    align-self: center;
    text-decoration: none;
  }

  &__score-line {
    margin: 0 0 var(--space-2);
    font-weight: 600;
  }

  &__note {
    color: var(--color-muted);
    font-style: italic;
  }
}

@media (max-width: 560px) {
  .detail {
    &__card {
      padding: var(--space-4);
    }

    &__header {
      flex-direction: column;
    }

    &__facts {
      grid-template-columns: minmax(0, 1fr);

      dd {
        margin-bottom: var(--space-2);
      }
    }
  }
}
</style>
