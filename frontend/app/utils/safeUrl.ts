/**
 * Returns the URL only if it is an absolute http(s) URL, otherwise null.
 * Vacancy data comes from external sources: `javascript:`, `data:`, relative or malformed URLs are never rendered.
 */
export function safeExternalUrl(value: string | null | undefined): string | null {
  if (!value) {
    return null
  }

  let url: URL
  try {
    url = new URL(value.trim())
  }
  catch {
    return null
  }

  return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null
}
