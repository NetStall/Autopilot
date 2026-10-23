import type { ApplicationPackage } from '~/types/package'

/** The whole e-mail as text: salutation, body, closing, name — what „Text kopieren" puts on the clipboard. */
export function emailText(pkg: Pick<ApplicationPackage, 'email'>): string {
  const e = pkg.email
  return `${e.salutation}\n\n${e.body}\n\n${e.closing}\n${e.senderName}`.trim()
}

/**
 * A mailto: link with subject and body (08c). Attachments cannot go into a mailto link — they come from the ZIP. Nothing
 * is sent by the tool: the link only opens my own mail program.
 */
export function mailtoLink(pkg: Pick<ApplicationPackage, 'email'>): string {
  const to = pkg.email.recipient ?? ''
  return `mailto:${encodeURIComponent(to).replaceAll('%40', '@')}?subject=${encodeURIComponent(pkg.email.subject)}&body=${encodeURIComponent(emailText(pkg))}`
}

/** „E-Mail" / „Portal" … for „Als gesendet markieren" (stored as the application's channel). */
export function sentChannel(channel: string): string {
  return channel === 'Email' ? 'E-Mail' : channel === 'Form' ? 'Formular' : channel === 'External' ? 'Extern' : 'Portal'
}
