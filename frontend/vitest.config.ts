import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'
import { defineVitestProject } from '@nuxt/test-utils/config'

// Two projects: fast pure-logic tests in Node, and component tests in a Nuxt environment (happy-dom).
export default defineConfig({
  test: {
    projects: [
      {
        test: {
          name: 'unit',
          include: ['test/*.test.ts'],
          environment: 'node',
        },
      },
      await defineVitestProject({
        test: {
          name: 'nuxt',
          include: ['test/nuxt/**/*.test.ts'],
          environment: 'nuxt',
          // With the persistent module cache the Nuxt entry stub of @nuxt/test-utils 4.3.3 fails on every run after
          // the first (fixed upstream in 4.3.4, nuxt/test-utils#1856). Remove when updating.
          fsModuleCache: false,
          environmentOptions: {
            nuxt: {
              rootDir: fileURLToPath(new URL('.', import.meta.url)),
              domEnvironment: 'happy-dom',
              overrides: { runtimeConfig: { public: { apiBase: 'http://api.test' } } },
            },
          },
        },
      }),
    ],
  },
})
