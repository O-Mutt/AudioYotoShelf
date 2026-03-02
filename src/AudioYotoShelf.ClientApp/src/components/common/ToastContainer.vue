<script setup lang="ts">
import { useToast } from '@/composables/useToast'

const { toasts, dismiss } = useToast()

function variantClasses(variant: string) {
  switch (variant) {
    case 'success': return 'bg-green-600 text-white'
    case 'error': return 'bg-red-600 text-white'
    case 'info': return 'bg-blue-600 text-white'
    default: return 'bg-gray-700 text-white'
  }
}

function variantIcon(variant: string) {
  switch (variant) {
    case 'success': return '✓'
    case 'error': return '✕'
    case 'info': return 'ℹ'
    default: return ''
  }
}
</script>

<template>
  <Teleport to="body">
    <div class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
      <TransitionGroup
        enter-active-class="transition-all duration-300 ease-out"
        leave-active-class="transition-all duration-200 ease-in"
        enter-from-class="translate-x-full opacity-0"
        leave-to-class="translate-x-full opacity-0"
      >
        <div
          v-for="toast in toasts"
          :key="toast.id"
          @click="dismiss(toast.id)"
          class="rounded-lg px-4 py-3 shadow-lg cursor-pointer flex items-center gap-2 min-w-[280px]"
          :class="variantClasses(toast.variant)"
        >
          <span class="font-bold text-lg leading-none">{{ variantIcon(toast.variant) }}</span>
          <span class="text-sm flex-1">{{ toast.message }}</span>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>
