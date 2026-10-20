import type { ApplicationDocuments, Letter } from '~/types/documents'

export const letter: Letter = {
  subject: 'Bewerbung als Full-Stack-Entwickler (m/w/d)',
  salutation: 'Sehr geehrte Damen und Herren,',
  paragraphs: ['Absatz eins mit example.org.', 'Ich habe ein Team von 12 Entwicklern geleitet.', 'Ganz offen: Angular kenne ich noch nicht.'],
  closing: 'Mit freundlichen Grüßen',
  formality: 'Sie',
  language: 'De',
  factsUsed: ['personal.website'],
  gapsMentioned: ['Angular'],
}

export function documents(overrides: Partial<ApplicationDocuments> = {}): ApplicationDocuments {
  return {
    applicationId: 'app-1',
    vacancyId: 'vac-1',
    company: 'Beispiel GmbH',
    position: 'Full-Stack-Entwickler (m/w/d)',
    status: 'Draft',
    statusLabel: 'Entwurf',
    version: 3,
    letter,
    cvOptions: { focus: null, hiddenBullets: [], shownBullets: [], bulletOrder: [], hiddenSkillGroups: [], skillGroupOrder: [] },
    cv: {
      language: 'De',
      focus: 'fullstack',
      availableFocuses: ['fullstack', 'backend'],
      roleLine: 'Full-Stack-Entwickler · C# / .NET · Vue',
      summary: 'Full-Stack-Entwickler mit 4 Jahren Erfahrung.',
      experience: [{
        id: 'exp0',
        title: 'Softwareentwickler',
        company: 'Muster Software GmbH',
        where: 'Regensburg',
        period: '04/2021 – 03/2025',
        bullets: [
          { id: 'exp0.b0', text: 'Entwickelte REST-APIs.', score: 3, hidden: false, autoHidden: false },
          { id: 'exp0.b1', text: 'Migrierte zu Vue 3.', score: 2, hidden: false, autoHidden: false },
        ],
      }],
      skillGroups: [{ name: 'Backend', skills: ['C#', '.NET'], hidden: false }, { name: 'Frontend', skills: ['Vue'], hidden: false }],
      untranslatedTexts: 0,
    },
    warnings: [
      { code: 'invented-number', message: 'Die Angabe „12“ steht weder im Profil noch in der Stellenanzeige.', paragraph: 1, severity: 'Warning' },
      { code: 'cliche', message: 'Floskel – lieber konkret werden.', paragraph: 0, severity: 'Hint' },
    ],
    hasOpenWarnings: true,
    approvedAt: null,
    warningsAccepted: false,
    letterFile: null,
    cvFile: null,
    letterPages: 1,
    cvPages: 1,
    model: 'claude-sonnet-5-5',
    promptVersion: 'letter.v1',
    usage: { inputTokens: 3000, outputTokens: 900, calls: 1, costUsd: 0.015, tokenBudget: 120000, callsToday: 1, dailyCallLimit: 30 },
    claudeConfigured: true,
    cheatSheet: [
      { key: 'subject', label: 'Betreff', value: 'Bewerbung als Full-Stack-Entwickler (m/w/d)', multiline: false },
      { key: 'email', label: 'E-Mail-Text', value: 'Sehr geehrte Damen und Herren,\n\nanbei …', multiline: true },
      { key: 'salary', label: 'Gehaltsvorstellung', value: '50.000–60.000 € brutto/Jahr', multiline: false },
    ],
    revisions: [
      { number: 2, kind: 'Saved', createdAt: '2026-10-20T08:05:00Z', note: null, model: null, inputTokens: 0, outputTokens: 0 },
      { number: 1, kind: 'Generated', createdAt: '2026-10-20T08:00:00Z', note: null, model: 'claude-sonnet-5-5', inputTokens: 3000, outputTokens: 900 },
    ],
    updatedAt: '2026-10-20T08:05:00Z',
    ...overrides,
  }
}
