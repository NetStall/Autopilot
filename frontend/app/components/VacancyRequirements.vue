<script setup lang="ts">
import type { VacancyExtraction } from '~/types/extraction'

/**
 * „Anforderungen": what the model extracted (source „KI") or, without an extraction, a note that the rules decide.
 * „Mit KI analysieren" queues this vacancy ahead of the background run and reloads when it is done.
 */
const props = defineProps<{
  vacancyId: string
  extraction: VacancyExtraction | null | undefined
  source: 'Rules' | 'Ai' | undefined
  hasDescription: boolean
}>()
const emit = defineEmits<{ analysed: [] }>()

const api = useVacanciesApi()
const { data: status, refresh: refreshStatus } = useAsyncData(`extraction-status-${props.vacancyId}`, () => api.extractionStatus(), { server: false, lazy: true })
const waiting = ref(false)
const error = ref<string | null>(null)
let timer: ReturnType<typeof setTimeout> | undefined

const levels: Record<string, string> = { Junior: 'Junior', Mid: 'Mid-Level', Senior: 'Senior', Lead: 'Lead' }
const employment: Record<string, string> = {
  FullTime: 'Vollzeit', PartTime: 'Teilzeit', Contract: 'Freiberuflich/befristet', Internship: 'Praktikum', WorkingStudent: 'Werkstudent',
}

const result = computed(() => props.extraction?.result ?? null)
const german = computed(() => {
  const r = result.value
  return !r ? null : r.germanNotRequired ? 'nicht nötig' : r.germanLevel
})

async function analyse() {
  error.value = null
  try {
    await api.extractVacancy(props.vacancyId)
    waiting.value = true
    poll()
  }
  catch (e: unknown) {
    error.value = describeApiError(e)
  }
}

function poll() {
  timer = setTimeout(async () => {
    await refreshStatus()
    const s = status.value
    const busy = s && (s.currentVacancyId === props.vacancyId || s.queued.includes(props.vacancyId))
    if (busy) {
      poll()
      return
    }
    waiting.value = false
    if (s?.lastError) {
      error.value = s.lastError
    }
    emit('analysed')
  }, 1500)
}

onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <div class="req">
    <p class="req__source">
      Quelle:
      <span class="req__badge" :class="source === 'Ai' ? 'req__badge--ai' : 'req__badge--rules'">
        {{ source === 'Ai' ? 'KI' : 'Regeln' }}
      </span>
      <span v-if="extraction" class="req__meta">{{ extraction.model }} · {{ extraction.promptVersion }}</span>
    </p>

    <template v-if="result">
      <p class="req__summary">
        {{ result.summary }}
      </p>
      <dl class="req__facts">
        <div v-if="result.mustHave.length">
          <dt>Muss</dt>
          <dd>{{ result.mustHave.join(', ') }}</dd>
        </div>
        <div v-if="result.niceToHave.length">
          <dt>Ein Plus</dt>
          <dd>{{ result.niceToHave.join(', ') }}</dd>
        </div>
        <div v-if="result.domainRequirements.length">
          <dt>Fachgebiet</dt>
          <dd>{{ result.domainRequirements.join(', ') }}</dd>
        </div>
        <div v-if="german">
          <dt>Deutsch</dt>
          <dd>{{ german }}</dd>
        </div>
        <div v-if="result.englishLevel">
          <dt>Englisch</dt>
          <dd>{{ result.englishLevel }}</dd>
        </div>
        <div v-if="result.minYears || levels[result.seniority]">
          <dt>Erfahrung</dt>
          <dd>{{ [result.minYears ? `mind. ${result.minYears} Jahre` : null, levels[result.seniority]].filter(Boolean).join(' · ') }}</dd>
        </div>
        <div v-if="result.homeOfficePercent">
          <dt>Homeoffice</dt>
          <dd>bis {{ result.homeOfficePercent }} %</dd>
        </div>
        <div v-if="employment[result.employmentType]">
          <dt>Anstellung</dt>
          <dd>{{ employment[result.employmentType] }}</dd>
        </div>
      </dl>
    </template>
    <p v-else class="req__note">
      Die Anforderungen kommen aus den Regeln (Abschnitte, Pflicht-Markierungen, Technologie-Katalog).
    </p>

    <div v-if="status?.available" class="req__actions">
      <button type="button" class="button" :disabled="waiting || !hasDescription" @click="analyse">
        {{ waiting ? 'KI analysiert …' : extraction ? 'Erneut mit KI analysieren' : 'Mit KI analysieren' }}
      </button>
      <span v-if="!hasDescription" class="req__note">Erst „Beschreibung einfügen".</span>
    </div>
    <p v-if="error" class="req__error" role="alert">
      {{ error }}
    </p>
  </div>
</template>

<style scoped lang="scss">
.req {
  display: grid;
  gap: 0.5rem;

  &__badge {
    padding: 0.1rem 0.45rem;
    border-radius: 999px;
    border: 1px solid var(--color-border);
    font-size: 0.85em;
    font-weight: 600;
  }

  &__meta,
  &__note {
    color: var(--color-muted);
    font-size: 0.9em;
  }

  &__facts {
    display: grid;
    gap: 0.25rem;
    margin: 0;

    div {
      display: grid;
      grid-template-columns: 8rem 1fr;
      gap: 0.5rem;
    }

    dt {
      font-weight: 600;
    }

    dd {
      margin: 0;
    }
  }

  &__error {
    color: var(--color-error-text);
  }
}
</style>
