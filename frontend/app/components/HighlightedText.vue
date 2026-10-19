<script setup lang="ts">
const props = withDefaults(defineProps<{
  text: string
  keywords?: readonly string[]
}>(), { keywords: () => [] })

const segments = computed(() => highlightSegments(props.text, props.keywords))
</script>

<!-- Text segments only, never v-html: titles come from an external API. -->
<template>
  <template v-for="(segment, index) in segments" :key="index">
    <mark v-if="segment.highlight" class="highlight">{{ segment.text }}</mark>
    <template v-else>
      {{ segment.text }}
    </template>
  </template>
</template>
