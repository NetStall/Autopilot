<script setup lang="ts">
import type { Application, ApplicationStatus } from '~/types/application'

const props = defineProps<{ applicationId: string }>()
const emit = defineEmits<{ close: [], changed: [application: Application] }>()

const api = useVacanciesApi()
const toasts = useToasts()
const { data: application, refresh } = useAsyncData(
  () => `application-${props.applicationId}`,
  () => api.getApplication(props.applicationId),
  { server: false, watch: [() => props.applicationId] },
)

const newStatus = ref<ApplicationStatus | ''>('')
const note = ref('')
const saving = ref(false)
const dialog = ref<HTMLElement | null>(null)

watch(application, (value) => {
  if (value) {
    newStatus.value = value.status
  }
}, { immediate: true })

async function saveStatus() {
  if (!application.value || !newStatus.value) {
    return
  }
  saving.value = true
  try {
    const updated = await api.changeApplicationStatus(application.value.id, newStatus.value, note.value)
    application.value = updated
    note.value = ''
    emit('changed', updated)
    toasts.show(`Status: ${updated.statusLabel}`)
  }
  catch (error: unknown) {
    toasts.error(`Status konnte nicht gespeichert werden: ${describeApiError(error)}`)
  }
  finally {
    saving.value = false
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    emit('close')
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown)
  void nextTick(() => dialog.value?.focus())
})
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <div class="drawer__backdrop" @click.self="emit('close')">
    <aside ref="dialog" class="drawer" role="dialog" aria-modal="true" aria-labelledby="drawer-title" tabindex="-1">
      <div class="drawer__head">
        <h2 id="drawer-title">
          {{ application?.company ?? 'Bewerbung' }}
        </h2>
        <button type="button" class="button button--secondary button--small" @click="emit('close')">
          Schließen
        </button>
      </div>

      <template v-if="application">
        <p class="drawer__position">
          {{ application.position }}
        </p>
        <p>
          <span class="status" :class="`status--${statusTone(application.status)}`">{{ application.statusLabel }}</span>
          <span v-if="application.isFollowUpDue" class="status status--due">Follow-up fällig</span>
        </p>

        <dl class="drawer__facts">
          <dt>Beworben</dt>
          <dd>{{ formatDate(application.appliedAt) }}</dd>
          <dt>Letzte Aktion</dt>
          <dd>{{ formatDate(application.lastActionAt) }}</dd>
          <dt>Follow-up</dt>
          <dd>{{ application.followUpAt ? formatDate(application.followUpAt) : '–' }}</dd>
          <dt>Kanal</dt>
          <dd>{{ application.channel ?? '–' }}</dd>
          <dt>Ort</dt>
          <dd>{{ application.location ?? '–' }}</dd>
          <dt>Kontakt</dt>
          <dd>{{ [application.contactName, application.contactEmail].filter(Boolean).join(' · ') || '–' }}</dd>
          <dt>Gehalt</dt>
          <dd>{{ application.salaryExpectation ? `${application.salaryExpectation.toLocaleString('de-DE')} €` : '–' }}</dd>
          <dt>Unterlagen</dt>
          <dd>{{ application.documents.join(', ') || '–' }}</dd>
          <dt>Link</dt>
          <dd>
            <a v-if="safeExternalUrl(application.url)" :href="safeExternalUrl(application.url)!" target="_blank" rel="noopener noreferrer">Stellenanzeige</a>
            <template v-else>
              –
            </template>
            <NuxtLink v-if="application.vacancyId" :to="`/vacancies/${application.vacancyId}`" class="drawer__vacancy">
              Stelle im Tool
            </NuxtLink>
          </dd>
          <dt>Nächster Schritt</dt>
          <dd>{{ application.nextStep ?? '–' }}</dd>
        </dl>

        <section v-if="application.notes" aria-labelledby="notes-heading">
          <h3 id="notes-heading">
            Notizen
          </h3>
          <p class="drawer__notes">
            {{ application.notes }}
          </p>
        </section>

        <form class="drawer__status" @submit.prevent="saveStatus">
          <h3>Status ändern</h3>
          <label for="drawer-status">Neuer Status</label>
          <select id="drawer-status" v-model="newStatus" class="input">
            <option v-for="option in APPLICATION_STATUSES" :key="option.value" :value="option.value">
              {{ option.label }}
            </option>
          </select>
          <label for="drawer-note">Notiz (optional)</label>
          <input id="drawer-note" v-model="note" class="input" maxlength="2000" placeholder="z. B. Follow-up per E-Mail gesendet">
          <button type="submit" class="button button--small" :disabled="saving">
            Speichern
          </button>
        </form>

        <section aria-labelledby="history-heading">
          <h3 id="history-heading">
            Verlauf
          </h3>
          <ol class="drawer__history">
            <li v-for="(event, index) in application.events ?? []" :key="index">
              <time :datetime="event.at">{{ formatDate(event.at) }}</time> — {{ event.toLabel }}
              <span v-if="event.note" class="drawer__event-note">{{ event.note }}</span>
            </li>
          </ol>
        </section>
      </template>
      <p v-else role="status">
        Wird geladen …
      </p>
      <button type="button" class="visually-hidden" @click="refresh()">
        Neu laden
      </button>
    </aside>
  </div>
</template>

<style scoped lang="scss">
.drawer {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  width: min(32rem, 100%);
  overflow-y: auto;
  padding: var(--space-4);
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: -4px 0 16px rgb(0 0 0 / 15%);

  &:focus-visible {
    outline: 3px solid var(--color-focus);
  }

  &__backdrop {
    position: fixed;
    inset: 0;
    z-index: 20;
    background: rgb(0 0 0 / 25%);
  }

  &__head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-3);

    h2 {
      margin: 0;
      font-size: 1.2rem;
    }
  }

  &__position {
    margin: var(--space-1) 0;
    color: var(--color-muted);
  }

  h3 {
    margin: var(--space-4) 0 var(--space-2);
    font-size: 1rem;
  }

  &__facts {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    gap: var(--space-1) var(--space-3);

    dt {
      color: var(--color-muted);
    }

    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
  }

  &__vacancy {
    margin-left: var(--space-2);
  }

  &__notes {
    white-space: pre-line;
  }

  &__status {
    display: grid;
    gap: var(--space-1);

    label {
      font-size: 0.85rem;
      font-weight: 600;
    }

    button {
      justify-self: start;
      margin-top: var(--space-2);
    }
  }

  &__history {
    margin: 0;
    padding-left: var(--space-4);
  }

  &__event-note {
    display: block;
    color: var(--color-muted);
    font-size: 0.85rem;
  }
}
</style>
