<script setup lang="ts">
useHead({ title: 'Profil · Job Autopilot' })

const api = useVacanciesApi()
const { data, status, error, refresh } = useAsyncData('profile', () => api.profile(), { server: false, lazy: true })

const profile = computed(() => data.value?.profile ?? null)
const skillGroups = computed(() => (profile.value ? skillsByLevel(profile.value.skills) : []))
const statusLabel = computed(() => {
  switch (data.value?.status) {
    case 'Loaded': return 'geladen'
    case 'Missing': return 'nicht gefunden'
    case 'Invalid': return 'fehlerhaft'
    default: return ''
  }
})
</script>

<template>
  <main class="profile">
    <div class="profile__header">
      <h1>Profil</h1>
      <button type="button" class="button button--secondary button--small" @click="refresh()">
        Neu laden
      </button>
    </div>
    <p class="profile__hint">
      Die einzige Quelle für Fakten in Bewertungen und (später) Anschreiben. Bearbeitet wird die Datei
      <code>{{ data?.fileName || 'data/profile.json' }}</code>; Änderungen werden automatisch übernommen.
    </p>

    <div v-if="status === 'idle' || status === 'pending'" class="state" role="status">
      Profil wird geladen …
    </div>
    <div v-else-if="error" class="state state--error" role="alert">
      Das Profil konnte nicht geladen werden. {{ describeApiError(error) }}
    </div>

    <template v-else-if="data">
      <section
        v-if="data.errors.length > 0 || data.warnings.length > 0"
        class="profile__problems"
        :class="{ 'profile__problems--error': data.errors.length > 0 }"
        :role="data.errors.length > 0 ? 'alert' : 'status'"
        aria-labelledby="problems-heading"
      >
        <h2 id="problems-heading">
          Profil {{ statusLabel }}
        </h2>
        <ul>
          <li v-for="(problem, index) in data.errors" :key="`e${index}`">
            {{ problem }}
          </li>
          <li v-for="(warning, index) in data.warnings" :key="`w${index}`" class="profile__warning">
            Hinweis: {{ warning }}
          </li>
        </ul>
      </section>

      <template v-if="profile">
        <section class="profile__card" aria-labelledby="personal-heading">
          <h2 id="personal-heading">
            {{ profile.personal.name }}
          </h2>
          <p class="profile__meta">
            {{ profile.personal.city }} · {{ profile.personal.email }}
            <span v-if="data.experienceYears != null"> · {{ data.experienceYears }} Jahre Berufserfahrung</span>
          </p>
          <ul class="profile__links">
            <li v-for="link in [profile.personal.website, profile.personal.github, profile.personal.linkedin]" :key="link ?? ''">
              <a v-if="safeExternalUrl(link)" :href="safeExternalUrl(link)!" target="_blank" rel="noopener noreferrer">{{ link }}</a>
            </li>
          </ul>
          <dl class="profile__summary">
            <template v-for="(text, focus) in profile.summary" :key="focus">
              <dt>{{ focus }}</dt>
              <dd>{{ text }}</dd>
            </template>
          </dl>
        </section>

        <section class="profile__card" aria-labelledby="skills-heading">
          <h2 id="skills-heading">
            Fähigkeiten
          </h2>
          <div v-for="group in skillGroups" :key="group.level" class="profile__skill-group">
            <h3>{{ skillLevelLabel(group.level) }}</h3>
            <ul class="profile__skills">
              <li v-for="skill in group.skills" :key="skill.name" class="badge" :class="{ 'badge--intermediary': group.level === 'None' }" :title="skill.aliases.join(', ')">
                {{ skill.name }}<span v-if="skill.years"> · {{ skill.years }} J.</span>
              </li>
            </ul>
          </div>
        </section>

        <section class="profile__card" aria-labelledby="gaps-heading">
          <h2 id="gaps-heading">
            Bekannte Lücken – ehrliche Formulierungen
          </h2>
          <dl class="profile__gaps">
            <template v-for="gap in profile.knownGaps" :key="gap.skill">
              <dt>{{ gap.skill }}</dt>
              <dd>
                <span lang="de">DE: {{ gap.de }}</span><br>
                <span lang="en">EN: {{ gap.en }}</span>
              </dd>
            </template>
          </dl>
        </section>

        <section class="profile__card" aria-labelledby="experience-heading">
          <h2 id="experience-heading">
            Berufserfahrung
          </h2>
          <article v-for="job in profile.experience" :key="`${job.company}-${job.from}`" class="profile__job">
            <h3>{{ job.title }} · {{ job.company }}</h3>
            <p class="profile__meta">
              {{ formatPeriod(job.from, job.to) }}<span v-if="job.location"> · {{ job.location }}</span><span v-if="job.remote"> · remote</span>
            </p>
            <ul>
              <li v-for="bullet in job.bullets" :key="bullet.text">
                {{ bullet.text }}
                <span class="profile__tags">{{ bullet.tags.join(' · ') }}</span>
              </li>
            </ul>
          </article>
        </section>

        <section class="profile__card" aria-labelledby="more-heading">
          <h2 id="more-heading">
            Sprachen, Projekte, Ausbildung, Wünsche
          </h2>
          <dl class="profile__facts">
            <dt>Sprachen</dt>
            <dd>{{ profile.languages.map(l => `${l.name} ${l.level === 'Native' ? 'Muttersprache' : l.level}`).join(' · ') }}</dd>
            <dt>Projekte</dt>
            <dd>{{ profile.projects.map(p => p.name).join(' · ') || '–' }}</dd>
            <dt>Ausbildung</dt>
            <dd>{{ profile.education.map(e => `${e.degree} (${formatPeriod(e.from, e.to)})`).join(' · ') || '–' }}</dd>
            <dt>Gehalt</dt>
            <dd>{{ profile.preferences.salaryMin ?? '–' }} – {{ profile.preferences.salaryMax ?? '–' }} €</dd>
            <dt>Regionen</dt>
            <dd>{{ profile.preferences.regions.join(', ') || '–' }} (max. {{ profile.preferences.maxCommuteMinutes ?? '–' }} Min.)</dd>
            <dt>Arbeitsort</dt>
            <dd>{{ profile.preferences.remote }}</dd>
            <dt>Frühester Start</dt>
            <dd>{{ profile.preferences.earliestStart ?? '–' }}</dd>
            <dt>Vermeiden</dt>
            <dd>{{ profile.preferences.avoidEmployerTypes.map(employerTypeLabel).join(', ') || '–' }}</dd>
          </dl>
        </section>
      </template>
    </template>
  </main>
</template>

<style scoped lang="scss">
.profile {
  max-width: var(--content-width);
  margin: 0 auto;
  padding: var(--space-5) var(--space-4) var(--space-6);

  &__header {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--space-3);

    h1 {
      margin: 0;
      font-size: 1.6rem;
    }
  }

  &__hint,
  &__meta {
    color: var(--color-muted);
  }

  &__card,
  &__problems {
    margin-top: var(--space-4);
    padding: var(--space-4);
    background: var(--color-surface);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);

    h2 {
      margin: 0 0 var(--space-2);
      font-size: 1.15rem;
    }

    h3 {
      margin: var(--space-3) 0 var(--space-1);
      font-size: 1rem;
    }
  }

  &__problems--error {
    border-color: var(--color-error-text);
  }

  &__warning {
    color: var(--color-warning-text);
  }

  &__links,
  &__skills {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  &__summary,
  &__gaps,
  &__facts {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    gap: var(--space-2) var(--space-4);
    margin: var(--space-3) 0 0;

    dt {
      color: var(--color-muted);
    }

    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
  }

  &__tags {
    margin-left: var(--space-2);
    font-size: 0.8rem;
    color: var(--color-muted);
  }
}

@media (max-width: 560px) {
  .profile {
    &__summary,
    &__gaps,
    &__facts {
      grid-template-columns: minmax(0, 1fr);
    }
  }
}
</style>
