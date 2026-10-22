import { describe, expect, it } from 'vitest'
import { emailText, mailtoLink, sentChannel } from '../app/utils/package'

const pkg = {
  email: {
    recipient: 'jobs@beispiel.de', subject: 'Bewerbung als Entwickler (m/w/d)', salutation: 'Sehr geehrte Frau Müller,',
    body: 'anbei meine Bewerbung.', closing: 'Mit freundlichen Grüßen', senderName: 'Alex Beispiel', attachments: [],
  },
}

describe('package texts (08c)', () => {
  it('puts salutation, body, closing and name into the e-mail text', () => {
    expect(emailText(pkg)).toBe('Sehr geehrte Frau Müller,\n\nanbei meine Bewerbung.\n\nMit freundlichen Grüßen\nAlex Beispiel')
  })

  it('builds a mailto link with subject and body, without attachments', () => {
    const link = mailtoLink(pkg)
    expect(link.startsWith('mailto:jobs@beispiel.de?subject=Bewerbung%20als%20Entwickler')).toBe(true)
    expect(decodeURIComponent(link.split('body=')[1]!)).toBe(emailText(pkg))
    expect(link).not.toContain('attach')
  })

  it('names the channel for „Als gesendet markieren"', () => {
    expect(sentChannel('Email')).toBe('E-Mail')
    expect(sentChannel('Portal')).toBe('Portal')
  })
})
