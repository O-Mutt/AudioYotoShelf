<script setup lang="ts">
import { useConfirm } from '@/composables/useConfirm'

const { isOpen, options, handleConfirm, handleCancel } = useConfirm()
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition-opacity duration-200"
      leave-active-class="transition-opacity duration-150"
      enter-from-class="opacity-0"
      leave-to-class="opacity-0"
    >
      <div
        v-if="isOpen"
        class="fixed inset-0 z-50 flex items-center justify-center"
      >
        <!-- Backdrop -->
        <div class="absolute inset-0 bg-black/50" @click="handleCancel" />

        <!-- Dialog -->
        <div class="relative bg-white rounded-xl shadow-xl max-w-md w-full mx-4 p-6">
          <h3 class="text-lg font-semibold text-gray-900">{{ options.title }}</h3>
          <p class="mt-2 text-sm text-gray-600">{{ options.message }}</p>

          <div class="mt-6 flex justify-end gap-3">
            <button
              @click="handleCancel"
              class="btn-secondary text-sm"
            >
              {{ options.cancelText }}
            </button>
            <button
              @click="handleConfirm"
              class="text-sm px-4 py-2 rounded-lg font-medium text-white transition-colors"
              :class="options.variant === 'danger'
                ? 'bg-red-600 hover:bg-red-700'
                : 'bg-yoto-blue hover:bg-blue-600'"
            >
              {{ options.confirmText }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
