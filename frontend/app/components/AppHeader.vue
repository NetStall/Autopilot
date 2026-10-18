<template>
  <header class="header">
    <div class="header__inner">
      <NuxtLink to="/" class="header__brand">
        Job Autopilot
      </NuxtLink>
      <ImportButton />
    </div>
  </header>
</template>

<style scoped lang="scss">
.header {
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);

  &__inner {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-3);
    max-width: var(--content-width);
    margin: 0 auto;
    padding: var(--space-3) var(--space-4);
  }

  &__brand {
    font-size: 1.2rem;
    font-weight: 700;
    color: var(--color-text);
    text-decoration: none;
  }
}
</style>
