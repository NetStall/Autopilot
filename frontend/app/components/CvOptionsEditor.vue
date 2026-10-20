<script setup lang="ts">
import type { Cv, CvOptions } from '~/types/documents'

defineProps<{
  cv: Cv
  disabled?: boolean
}>()

const options = defineModel<CvOptions>({ required: true })
</script>

<template>
  <section class="cv" aria-labelledby="cv-heading">
    <h2 id="cv-heading">
      Lebenslauf
    </h2>
    <p class="cv__role">
      {{ cv.roleLine }}
    </p>

    <label for="cv-focus">Schwerpunkt (Profiltext)</label>
    <select
      id="cv-focus"
      class="input cv__focus"
      :value="options.focus ?? cv.focus"
      :disabled="disabled"
      @change="options = setFocus(options, ($event.target as HTMLSelectElement).value)"
    >
      <option v-for="focus in cv.availableFocuses" :key="focus" :value="focus">
        {{ focusLabel(focus) }}
      </option>
    </select>
    <p v-if="cv.summary" class="cv__summary">
      {{ cv.summary }}
    </p>

    <fieldset v-for="entry in cv.experience" :key="entry.id" class="cv__entry">
      <legend>{{ entry.title }} · {{ entry.company }} <span class="cv__muted">({{ entry.period }})</span></legend>
      <ul class="cv__bullets">
        <li v-for="(bullet, index) in entry.bullets" :key="bullet.id" class="cv__bullet" :class="{ 'cv__bullet--hidden': bullet.hidden }">
          <label class="cv__check">
            <input
              type="checkbox"
              :checked="!bullet.hidden"
              :disabled="disabled"
              @change="options = toggleBullet(options, bullet)"
            >
            <span>{{ bullet.text }}</span>
          </label>
          <span v-if="bullet.autoHidden" class="cv__muted">(automatisch ausgeblendet)</span>
          <span class="cv__moves">
            <button
              type="button"
              class="button button--secondary button--small"
              :disabled="disabled || index === 0"
              :aria-label="`Nach oben: ${bullet.text}`"
              @click="options = moveBullet(options, entry, bullet.id, -1)"
            >↑</button>
            <button
              type="button"
              class="button button--secondary button--small"
              :disabled="disabled || index === entry.bullets.length - 1"
              :aria-label="`Nach unten: ${bullet.text}`"
              @click="options = moveBullet(options, entry, bullet.id, 1)"
            >↓</button>
          </span>
        </li>
      </ul>
    </fieldset>

    <fieldset class="cv__entry">
      <legend>Kenntnisse</legend>
      <ul class="cv__bullets">
        <li v-for="(group, index) in cv.skillGroups" :key="group.name" class="cv__bullet" :class="{ 'cv__bullet--hidden': group.hidden }">
          <label class="cv__check">
            <input type="checkbox" :checked="!group.hidden" :disabled="disabled" @change="options = toggleSkillGroup(options, group)">
            <span><strong>{{ group.name }}</strong>: {{ group.skills.join(', ') }}</span>
          </label>
          <span class="cv__moves">
            <button
              type="button"
              class="button button--secondary button--small"
              :disabled="disabled || index === 0"
              :aria-label="`Nach oben: ${group.name}`"
              @click="options = moveSkillGroup(options, cv.skillGroups, group.name, -1)"
            >↑</button>
            <button
              type="button"
              class="button button--secondary button--small"
              :disabled="disabled || index === cv.skillGroups.length - 1"
              :aria-label="`Nach unten: ${group.name}`"
              @click="options = moveSkillGroup(options, cv.skillGroups, group.name, 1)"
            >↓</button>
          </span>
        </li>
      </ul>
    </fieldset>
  </section>
</template>

<style scoped lang="scss">
.cv {
  display: grid;
  gap: var(--space-2);

  h2 {
    margin: 0;
    font-size: 1.1rem;
  }

  > label {
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__role {
    margin: 0;
    color: var(--color-accent);
  }

  &__focus {
    max-width: 16rem;
  }

  &__summary {
    margin: 0;
    font-size: 0.9rem;
    color: var(--color-muted);
  }

  &__entry {
    margin: 0;
    padding: var(--space-2) 0 0;
    border: 0;

    legend {
      font-weight: 600;
      font-size: 0.9rem;
    }
  }

  &__bullets {
    display: grid;
    gap: var(--space-1);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  &__bullet {
    display: flex;
    align-items: flex-start;
    gap: var(--space-2);
    font-size: 0.9rem;

    &--hidden span {
      color: var(--color-muted);
      text-decoration: line-through;
    }
  }

  &__check {
    display: flex;
    flex: 1;
    gap: var(--space-2);
  }

  &__moves {
    display: inline-flex;
    gap: var(--space-1);
    text-decoration: none;
  }

  &__muted {
    color: var(--color-muted);
    font-size: 0.8rem;
    font-weight: 400;
  }
}
</style>
