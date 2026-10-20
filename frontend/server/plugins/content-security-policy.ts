/**
 * Content-Security-Policy for server-rendered pages.
 *
 * - script-src: only own files plus the SHA-256 of each inline script Nuxt renders into the page (runtime config,
 *   hydration) — no 'unsafe-inline' for scripts. Hashes are computed from the final HTML, so they always match.
 * - connect-src: the API origin from the runtime config (NUXT_PUBLIC_API_BASE), which is only known at start-up.
 * - img-src blob: for the PDF previews (images the page itself creates from API responses; no remote images).
 * - Skipped in dev mode: Vite's HMR client needs inline scripts and websockets.
 */
const INLINE_SCRIPT = /<script(?![^>]*\ssrc=)(?![^>]*type="application\/json")[^>]*>([\s\S]*?)<\/script>/gi

async function sha256Base64(text: string): Promise<string> {
  // Web Crypto (global in Node 24) — no Node-specific imports in server code.
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)))
  let binary = ''
  for (const byte of digest) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary)
}

async function hashInlineScripts(html: string): Promise<string[]> {
  const hashes = new Set<string>()
  for (const match of html.matchAll(INLINE_SCRIPT)) {
    const content = match[1] ?? ''
    if (content.length > 0) {
      hashes.add(`'sha256-${await sha256Base64(content)}'`)
    }
  }
  return [...hashes]
}

function originOf(url: string): string {
  try {
    return new URL(url).origin
  }
  catch {
    return ''
  }
}

export async function buildContentSecurityPolicy(html: string, apiBase: string): Promise<string> {
  const scriptHashes = await hashInlineScripts(html)
  return [
    `default-src 'self'`,
    `script-src 'self' ${scriptHashes.join(' ')}`.trim(),
    // Nuxt inlines component CSS into the HTML; styles cannot execute code.
    `style-src 'self' 'unsafe-inline'`,
    // blob: — the document previews (PNG fetched from the API and shown via URL.createObjectURL).
    `img-src 'self' data: blob:`,
    `font-src 'self'`,
    `connect-src 'self' ${originOf(apiBase)}`.trim(),
    `object-src 'none'`,
    `base-uri 'self'`,
    `form-action 'self'`,
    `frame-ancestors 'none'`,
  ].join('; ')
}

export default defineNitroPlugin((nitroApp) => {
  if (import.meta.dev) {
    return
  }

  nitroApp.hooks.hook('render:response', async (response, { event }) => {
    if (typeof response.body !== 'string') {
      return
    }
    const apiBase = String(useRuntimeConfig(event).public.apiBase ?? '')
    response.headers = {
      ...response.headers,
      'content-security-policy': await buildContentSecurityPolicy(response.body, apiBase),
    }
  })
})
