// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2026-10-01',
  devtools: { enabled: true },

  app: {
    head: {
      title: 'Job Autopilot',
      htmlAttrs: { lang: 'de' },
      meta: [{ name: 'viewport', content: 'width=device-width, initial-scale=1' }],
    },
  },

  css: ['~/assets/styles/main.scss'],

  // Overridden at runtime by NUXT_PUBLIC_API_BASE.
  runtimeConfig: {
    public: {
      apiBase: 'http://localhost:5080',
    },
  },

  // Static security headers for every response. The Content-Security-Policy depends on the runtime API URL and
  // on the inline scripts of each page, so it is set by server/plugins/content-security-policy.ts.
  routeRules: {
    '/**': {
      headers: {
        'X-Content-Type-Options': 'nosniff',
        'X-Frame-Options': 'DENY',
        'Referrer-Policy': 'strict-origin-when-cross-origin',
        'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=(), usb=(), browsing-topics=()',
        'Cross-Origin-Opener-Policy': 'same-origin',
      },
    },
  },

  typescript: {
    strict: true,
  },
})
