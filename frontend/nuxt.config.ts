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

  typescript: {
    strict: true,
  },
})
