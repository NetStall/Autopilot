import { describe, expect, it } from 'vitest'
import { safeExternalUrl } from '../app/utils/safeUrl'

describe('safeExternalUrl', () => {
  it.each([
    ['https://www.arbeitsagentur.de/jobsuche/jobdetail/10000-1-S', 'https://www.arbeitsagentur.de/jobsuche/jobdetail/10000-1-S'],
    ['http://example.de/jobs/1', 'http://example.de/jobs/1'],
    ['  https://example.de/a b  ', 'https://example.de/a%20b'],
    ['HTTPS://EXAMPLE.DE/Job', 'https://example.de/Job'],
  ])('keeps absolute http(s) URL %s', (input, expected) => {
    expect(safeExternalUrl(input)).toBe(expected)
  })

  it.each([
    'javascript:alert(1)',
    'JavaScript:alert(document.cookie)',
    ' javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'vbscript:msgbox(1)',
    'file:///C:/Windows/win.ini',
    'ftp://example.de/job.txt',
    'mailto:jobs@example.de',
    '/jobs/1',
    'jobs/1',
    '//evil.example/jobs',
    'www.example.de/jobs',
    'not a url',
    '',
  ])('rejects %s', input => {
    expect(safeExternalUrl(input)).toBeNull()
  })

  it('rejects null and undefined', () => {
    expect(safeExternalUrl(null)).toBeNull()
    expect(safeExternalUrl(undefined)).toBeNull()
  })
})
