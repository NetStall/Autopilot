<script setup lang="ts">
import type { EmployerType } from '~/types/vacancy'

defineProps<{
  type: EmployerType
}>()
</script>

<template>
  <span class="badge" :class="{ 'badge--intermediary': isIntermediary(type) }">
    {{ employerTypeLabel(type) }}
  </span>
</template>
