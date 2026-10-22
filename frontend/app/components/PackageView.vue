<script setup lang="ts">
import type { ApplicationPackage } from '~/types/package'

/**
 * One „Bewerbungspaket" (08c): the e-mail part (recipient, subject, text to copy, mailto, attachments as ZIP after
 * „Freigeben") or the portal part (checklist, short texts to paste, the posting). Nothing is sent from here.
 */
const props = defineProps<{ pkg: ApplicationPackage }>()

const api = useVacanciesApi()
const copied = ref<string | null>(null)
const isEmail = computed(() => props.pkg.howToApply.channel === 'Email' && !props.pkg.howToApply.portalOnly)

async function copy(key: string, text: string) {
  try {
    await navigator.clipboard.writeText(text)
    copied.value = key
  }
  catch {
    copied.value = null
  }
}
</script>

<template>
  <div class="pkg">
    <p v-if="pkg.warnings.length || pkg.letterWarnings.some(w => w.severity === 'Warning')" class="pkg__warnings" role="status">
      Faktenprüfung: {{ [...pkg.warnings, ...pkg.letterWarnings.filter(w => w.severity === 'Warning')].map(w => w.message).join(' · ') }}
    </p>

    <section v-if="isEmail" class="pkg__part" aria-label="Bewerbung per E-Mail">
      <dl class="pkg__fields">
        <dt>An</dt>
        <dd>{{ pkg.email.recipient }}</dd>
        <dt>Betreff</dt>
        <dd>{{ pkg.email.subject }}</dd>
        <dt>Anhänge</dt>
        <dd>{{ pkg.email.attachments.join(', ') }}</dd>
      </dl>
      <pre class="pkg__text">{{ emailText(pkg) }}</pre>
      <div class="pkg__actions">
        <button type="button" class="button button--secondary button--small" @click="copy('email', emailText(pkg))">
          {{ copied === 'email' ? 'Kopiert ✓' : 'Text kopieren' }}
        </button>
        <a :href="mailtoLink(pkg)" class="button button--secondary button--small">In Mailprogramm öffnen (ohne Anhänge)</a>
        <a v-if="pkg.approved" :href="api.packageZipUrl(pkg.applicationId)" class="button button--secondary button--small">Anhänge herunterladen (ZIP)</a>
        <span v-else class="pkg__hint">Anhänge nach „Freigeben“</span>
      </div>
    </section>

    <section v-else class="pkg__part" aria-label="Bewerbung über Portal oder Formular">
      <p v-if="pkg.howToApply.portalOnly" class="pkg__hint">
        Nur über das Portal – E-Mail-Bewerbungen werden nicht berücksichtigt.
      </p>
      <ul class="pkg__checklist">
        <li v-for="item in pkg.portal.checklist" :key="item.label">
          <strong>{{ item.label }}</strong><template v-if="item.value">
            : {{ item.value }}
          </template>
        </li>
      </ul>
      <h4>Kurze Motivation ({{ pkg.portal.motivation.length }} / 1000)</h4>
      <pre class="pkg__text">{{ pkg.portal.motivation }}</pre>
      <button type="button" class="button button--secondary button--small" @click="copy('motivation', pkg.portal.motivation)">
        {{ copied === 'motivation' ? 'Kopiert ✓' : 'Motivation kopieren' }}
      </button>
      <h4>Über mich ({{ pkg.portal.aboutMe.length }} / 500)</h4>
      <pre class="pkg__text">{{ pkg.portal.aboutMe }}</pre>
      <button type="button" class="button button--secondary button--small" @click="copy('about', pkg.portal.aboutMe)">
        {{ copied === 'about' ? 'Kopiert ✓' : '„Über mich“ kopieren' }}
      </button>
      <p v-if="safeExternalUrl(pkg.portal.postingUrl)">
        <a :href="safeExternalUrl(pkg.portal.postingUrl)!" target="_blank" rel="noopener noreferrer">Zur Anzeige<span class="visually-hidden"> (neuer Tab)</span></a>
      </p>
    </section>
  </div>
</template>

<style scoped lang="scss">
.pkg {
  display: grid;
  gap: var(--space-3);

  &__fields {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: var(--space-1) var(--space-3);
    margin: 0;

    dt {
      font-weight: 600;
    }

    dd {
      margin: 0;
    }
  }

  &__text {
    white-space: pre-wrap;
    font-family: inherit;
    padding: var(--space-3);
    border: 1px solid var(--color-border);
    border-radius: var(--radius);
    margin: 0;
  }

  &__actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
    align-items: center;
  }

  &__hint {
    color: var(--color-muted);
    font-size: 0.9rem;
  }

  &__warnings {
    color: var(--color-warning-text);
  }
}
</style>
