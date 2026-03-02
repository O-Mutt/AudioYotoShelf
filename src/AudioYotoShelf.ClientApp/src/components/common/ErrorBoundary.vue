<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue'

const hasError = ref(false)
const errorMessage = ref('')

onErrorCaptured((err) => {
  hasError.value = true
  errorMessage.value = err instanceof Error ? err.message : String(err)
  console.error('[ErrorBoundary]', err)
  return false // Prevent error from propagating
})

function retry() {
  hasError.value = false
  errorMessage.value = ''
}
</script>

<template>
  <div v-if="hasError" class="text-center py-16 px-4">
    <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-16 w-16 text-red-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
      <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
    </svg>
    <h3 class="mt-4 text-lg font-medium text-gray-700">Something went wrong</h3>
    <p class="mt-2 text-sm text-gray-400 max-w-md mx-auto">
      An unexpected error occurred. Try refreshing the page or going back.
    </p>
    <p v-if="errorMessage" class="mt-2 text-xs text-gray-400 font-mono bg-gray-50 rounded p-2 max-w-md mx-auto">
      {{ errorMessage }}
    </p>
    <div class="mt-6 flex justify-center gap-3">
      <button @click="retry" class="btn-primary text-sm">Try Again</button>
      <router-link to="/library" class="btn-secondary text-sm">Go to Library</router-link>
    </div>
  </div>
  <slot v-else />
</template>
