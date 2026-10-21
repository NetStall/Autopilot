<script setup lang="ts">
import type { ChannelStatistics } from '~/types/statistics'

/**
 * „Bewerbungswege" (08a): how the current Top N of my list take applications, how many could be prepared end to end
 * (e-mail, not portal-only, with a description), and rules vs. the local model. Addresses are only shown.
 */
const TOPS = [{ value: 50, label: 'Top 50' }, { value: 100, label: 'Top 100' }, { value: 0, label: 'Alle sichtbaren' }] as const

const api = useVacanciesApi()
const top = ref<number>(100)
const stats = ref<ChannelStatistics | null>(null)
const error = ref<string | null>(null)

async function load() {
  error.value = null
  try {
    stats.value = await api.channelStatistics(top.value)
  }
  catch (e: unknown) {
    error.value = `Bewerbungswege nicht geladen: ${describeApiError(e)}`
  }
}

watch(top, load)
onMounted(load)
</script>

<template>
  <section class="channels" aria-labelledby="channels-heading">
    <div class="channels__head">
      <h2 id="channels-heading">
        Bewerbungswege
      </h2>
      <label for="channels-top" class="visually-hidden">Umfang</label>
      <select id="channels-top" v-model.number="top" class="input">
        <option v-for="option in TOPS" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
    </div>

    <p v-if="error" role="alert" class="channels__error">
      {{ error }}
    </p>
    <template v-else-if="stats">
      <p>
        <strong>{{ stats.emailReady }} von {{ stats.vacancies }} ({{ stats.emailReadyPercent }} %) vollständig vorbereitbar</strong>
        – per E-Mail, nicht „nur Portal", mit Beschreibung.
      </p>
      <table class="channels__table">
        <thead>
          <tr>
            <th scope="col">
              Weg
            </th>
            <th scope="col">
              Anzahl
            </th>
            <th scope="col">
              Anteil
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in stats.channels" :key="row.channel">
            <th scope="row">
              {{ CHANNEL_LABELS[row.channel] }}
            </th>
            <td>{{ row.count }}</td>
            <td>{{ row.percent }} %</td>
          </tr>
        </tbody>
      </table>

      <h3>Top 10 per E-Mail</h3>
      <ol v-if="stats.topEmailReady.length" class="channels__list">
        <li v-for="item in stats.topEmailReady" :key="item.vacancyId">
          <NuxtLink :to="`/vacancies/${item.vacancyId}`">
            {{ item.title }}
          </NuxtLink>
          · {{ item.company }} · {{ item.email }}<template v-if="item.contactName">
            · {{ item.contactName }}
          </template>
          <span class="channels__source">({{ SOURCE_LABELS[item.source] }})</span>
        </li>
      </ol>
      <p v-else>
        Keine Stelle mit E-Mail-Bewerbung.
      </p>

      <p v-if="stats.comparison.withAi" class="channels__compare">
        Regeln vs. KI ({{ stats.comparison.withAi }} mit KI-Analyse): gleicher Weg {{ stats.comparison.sameChannel }},
        gleiche Adresse {{ stats.comparison.bothSameEmail }}, nur Regeln {{ stats.comparison.rulesOnlyEmail }},
        nur KI {{ stats.comparison.aiOnlyEmail }}, verschieden {{ stats.comparison.differentEmail }},
        KI-Adresse verworfen (nicht im Text) {{ stats.comparison.aiEmailRejected }}.
      </p>
    </template>
  </section>
</template>

<style scoped lang="scss">
.channels {
  margin-top: var(--space-6);

  &__head {
    display: flex;
    align-items: baseline;
    gap: var(--space-3);
  }

  &__table {
    border-collapse: collapse;

    th,
    td {
      padding: var(--space-1) var(--space-3);
      text-align: left;
    }
  }

  &__source,
  &__compare {
    color: var(--color-muted);
    font-size: 0.9rem;
  }

  &__error {
    color: var(--color-error-text);
  }
}
</style>
