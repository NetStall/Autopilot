import { createHash } from 'node:crypto'
import { describe, expect, it, vi } from 'vitest'

// The plugin module registers itself with Nitro; only the pure builder is tested here.
vi.stubGlobal('defineNitroPlugin', (plugin: unknown) => plugin)
const { buildContentSecurityPolicy } = await import('../server/plugins/content-security-policy')

const sha = (s: string) => `'sha256-${createHash('sha256').update(s, 'utf8').digest('base64')}'`

describe('buildContentSecurityPolicy', () => {
  const html = [
    '<html><head>',
    '<script type="module" src="/_nuxt/entry.js" crossorigin></script>',
    '<script>window.__NUXT__={};window.__NUXT__.config={public:{apiBase:"http://localhost:5080"}}</script>',
    '<script type="application/json" data-nuxt-data="nuxt-app" id="__NUXT_DATA__">[{"state":1}]</script>',
    '</head><body></body></html>',
  ].join('')

  it('allows exactly the inline scripts of the page by hash, not unsafe-inline', async () => {
    const csp = await buildContentSecurityPolicy(html, 'http://localhost:5080')
    const scriptSrc = csp.split('; ').find(d => d.startsWith('script-src'))!

    expect(scriptSrc).toBe(
      `script-src 'self' ${sha('window.__NUXT__={};window.__NUXT__.config={public:{apiBase:"http://localhost:5080"}}')}`,
    )
    expect(scriptSrc).not.toContain('unsafe-inline')
    expect(scriptSrc).not.toContain('unsafe-eval')
  })

  it('lets the browser call only its own origin and the API origin', async () => {
    const csp = await buildContentSecurityPolicy(html, 'http://localhost:5080/some/path')

    expect(csp).toContain(`connect-src 'self' http://localhost:5080;`)
  })

  it('allows images only from itself, data: and blob: (document previews)', async () => {
    const csp = await buildContentSecurityPolicy(html, 'http://localhost:5080')

    expect(csp).toContain(`img-src 'self' data: blob:;`)
  })

  it('forbids framing, plugins and foreign form targets', async () => {
    const csp = await buildContentSecurityPolicy(html, 'http://localhost:5080')

    expect(csp).toContain(`frame-ancestors 'none'`)
    expect(csp).toContain(`object-src 'none'`)
    expect(csp).toContain(`form-action 'self'`)
    expect(csp).toContain(`base-uri 'self'`)
    expect(csp.startsWith(`default-src 'self'`)).toBe(true)
  })

  it('ignores an invalid API URL instead of writing it into the policy', async () => {
    const csp = await buildContentSecurityPolicy(html, 'not a url')

    expect(csp).toContain(`connect-src 'self';`)
  })
})
