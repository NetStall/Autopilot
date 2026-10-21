import type { ApplyChannel, ApplySource, HowToApply } from '~/types/statistics'

export const CHANNEL_LABELS: Record<ApplyChannel, string> = {
  Email: 'E-Mail',
  Portal: 'Portal',
  Form: 'Formular',
  External: 'Extern',
  Unknown: 'Unbekannt',
}

export const SOURCE_LABELS: Record<ApplySource, string> = {
  Field: 'Feld',
  Rule: 'Regel',
  Ai: 'KI',
  None: '–',
}

/** „per E-Mail an jobs@… (Ansprechpartnerin: Lisa)", „nur über Portal", … for the detail page (08a). */
export function howToApplyLine(how: HowToApply): string {
  const contact = how.contactName ? ` (Ansprechperson: ${how.contactName})` : ''
  if (how.portalOnly) {
    return `nur über Portal – keine E-Mail-Bewerbung${contact}`
  }
  switch (how.channel) {
    case 'Email':
      return `per E-Mail an ${how.email}${contact}`
    case 'Portal':
      return `über das Karriereportal${contact}`
    case 'Form':
      return `über ein Online-Formular${contact}`
    case 'External':
      return `über die verlinkte Anzeige${contact}`
    default:
      return `unbekannt${contact}`
  }
}
