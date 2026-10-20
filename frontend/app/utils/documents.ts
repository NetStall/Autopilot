import type { ApplicationStatus } from '../types/application'
import type { CvBullet, CvExperience, CvOptions, CvSkillGroup, DraftRevisionKind, DraftWarning, Letter } from '../types/documents'

/** Same limit as the backend fact guard: above this a letter likely needs a second page. */
export const MAX_LETTER_CHARACTERS = 2600

export const MAX_PARAGRAPH_LENGTH = 2000

/** Channels for „Als gesendet markieren". */
export const SEND_CHANNELS: readonly string[] = [
  'E-Mail',
  'Bewerbungsportal',
  'Firmenwebsite',
  'Arbeitsagentur',
  'Himalayas',
  'LinkedIn',
  'Persönlich',
  'Sonstiges',
]

/** Documents can be edited and approved until the application is sent. */
export function canEditDocuments(status: ApplicationStatus): boolean {
  return status === 'Draft' || status === 'Approved'
}

export function paragraphId(index: number): string {
  return `letter-paragraph-${index}`
}

/** „1.834 Zeichen · passt auf 1 Seite" — the server's page count wins once known. */
export function letterLength(letter: Letter, renderedPages?: number | null): { characters: number, tooLong: boolean, label: string } {
  const characters = [letter.subject, letter.salutation, ...letter.paragraphs, letter.closing].reduce((sum, t) => sum + t.length, 0)
  const tooLong = renderedPages != null ? renderedPages > 1 : characters > MAX_LETTER_CHARACTERS
  const count = characters.toLocaleString('de-DE')
  const pages = renderedPages != null
    ? (renderedPages > 1 ? `${renderedPages} Seiten – zu lang` : 'passt auf 1 Seite')
    : (tooLong ? 'wahrscheinlich länger als 1 Seite' : 'voraussichtlich 1 Seite')
  return { characters, tooLong, label: `${count} Zeichen · ${pages}` }
}

/** Warnings about one paragraph, for the marker next to its text area. */
export function warningsForParagraph(warnings: readonly DraftWarning[], index: number): DraftWarning[] {
  return warnings.filter(w => w.paragraph === index)
}

export function hasBlockingWarnings(warnings: readonly DraftWarning[]): boolean {
  return warnings.some(w => w.severity === 'Warning')
}

function without(list: readonly string[], value: string): string[] {
  return list.filter(v => v !== value)
}

function withValue(list: readonly string[], value: string): string[] {
  return list.includes(value) ? [...list] : [...list, value]
}

/**
 * Shows or hides a CV bullet. A bullet the builder hid (limit, one page) becomes „shown on purpose";
 * hiding removes that again.
 */
export function toggleBullet(options: CvOptions, bullet: CvBullet): CvOptions {
  if (bullet.hidden) {
    return {
      ...options,
      hiddenBullets: without(options.hiddenBullets, bullet.id),
      shownBullets: bullet.autoHidden ? withValue(options.shownBullets, bullet.id) : without(options.shownBullets, bullet.id),
    }
  }
  return {
    ...options,
    hiddenBullets: withValue(options.hiddenBullets, bullet.id),
    shownBullets: without(options.shownBullets, bullet.id),
  }
}

/** Moves a bullet up (-1) or down (+1) within its entry; the entry's whole order is then fixed. */
export function moveBullet(options: CvOptions, experience: CvExperience, bulletId: string, delta: -1 | 1): CvOptions {
  const ids = experience.bullets.map(b => b.id)
  const from = ids.indexOf(bulletId)
  const to = from + delta
  if (from < 0 || to < 0 || to >= ids.length) {
    return options
  }
  ;[ids[from], ids[to]] = [ids[to]!, ids[from]!]
  const others = options.bulletOrder.filter(id => !experience.bullets.some(b => b.id === id))
  return { ...options, bulletOrder: [...others, ...ids] }
}

export function toggleSkillGroup(options: CvOptions, group: CvSkillGroup): CvOptions {
  return {
    ...options,
    hiddenSkillGroups: group.hidden ? without(options.hiddenSkillGroups, group.name) : withValue(options.hiddenSkillGroups, group.name),
  }
}

export function moveSkillGroup(options: CvOptions, groups: readonly CvSkillGroup[], name: string, delta: -1 | 1): CvOptions {
  const names = groups.map(g => g.name)
  const from = names.indexOf(name)
  const to = from + delta
  if (from < 0 || to < 0 || to >= names.length) {
    return options
  }
  ;[names[from], names[to]] = [names[to]!, names[from]!]
  return { ...options, skillGroupOrder: names }
}

export function setFocus(options: CvOptions, focus: string): CvOptions {
  return { ...options, focus }
}

/** Unsaved changes compared with the last loaded state. */
export function isDocumentsDirty(letter: Letter, cv: CvOptions, saved: { letter: Letter, cvOptions: CvOptions } | null): boolean {
  return saved != null && (JSON.stringify(letter) !== JSON.stringify(saved.letter) || JSON.stringify(cv) !== JSON.stringify(saved.cvOptions))
}

export function cloneLetter(letter: Letter): Letter {
  return { ...letter, paragraphs: [...letter.paragraphs], factsUsed: [...letter.factsUsed], gapsMentioned: [...letter.gapsMentioned] }
}

export function cloneCvOptions(options: CvOptions): CvOptions {
  return {
    focus: options.focus,
    hiddenBullets: [...options.hiddenBullets],
    shownBullets: [...options.shownBullets],
    bulletOrder: [...options.bulletOrder],
    hiddenSkillGroups: [...options.hiddenSkillGroups],
    skillGroupOrder: [...options.skillGroupOrder],
  }
}

const REVISION_LABELS: Record<DraftRevisionKind, string> = {
  Generated: 'Erstellt',
  Rephrased: 'Absatz neu formuliert',
  Saved: 'Gespeichert',
  Restored: 'Wiederhergestellt',
}

export function revisionLabel(kind: DraftRevisionKind): string {
  return REVISION_LABELS[kind]
}

const FOCUS_LABELS: Record<string, string> = {
  fullstack: 'Full Stack',
  backend: 'Backend',
  ai: 'KI / AI',
}

export function focusLabel(focus: string): string {
  return FOCUS_LABELS[focus] ?? focus
}

/** „3.900 Tokens · ≈ 0,04 $". */
export function usageLabel(inputTokens: number, outputTokens: number, costUsd: number): string {
  const tokens = (inputTokens + outputTokens).toLocaleString('de-DE')
  const cost = costUsd.toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 3 })
  return `${tokens} Tokens · ≈ ${cost} $`
}
