<script setup lang="ts">
import { computed } from 'vue'
import { useConnectionStore } from '@/stores/connectionStore'
import { useRouter } from 'vue-router'

const connectionStore = useConnectionStore()
const router = useRouter()

const statusDot = computed(() => {
  if (connectionStore.isFullyConnected) return 'bg-green-400'
  if (connectionStore.isAbsConnected) return 'bg-yellow-400'
  return 'bg-red-400'
})

const statusText = computed(() => {
  if (connectionStore.isFullyConnected) return 'Connected'
  if (connectionStore.isAbsConnected) return 'ABS Only'
  return 'Not Connected'
})
</script>

<template>
  <nav class="bg-white shadow-sm border-b border-gray-100">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
      <div class="flex items-center justify-between h-16">
        <div class="flex items-center space-x-8">
          <router-link to="/" class="text-xl font-bold text-yoto-blue">
            AudioYotoShelf
          </router-link>

          <template v-if="connectionStore.isAbsConnected">
            <router-link
              to="/library"
              class="text-gray-600 hover:text-gray-900 font-medium"
              active-class="text-yoto-blue"
            >
              Library
            </router-link>
            <router-link
              to="/transfers"
              class="text-gray-600 hover:text-gray-900 font-medium"
              active-class="text-yoto-blue"
            >
              Transfers
            </router-link>
          </template>
        </div>

        <div class="flex items-center space-x-4">
          <div class="flex items-center space-x-2 text-sm text-gray-500">
            <span class="relative flex h-2.5 w-2.5">
              <span
                class="animate-ping absolute inline-flex h-full w-full rounded-full opacity-75"
                :class="statusDot"
              />
              <span class="relative inline-flex rounded-full h-2.5 w-2.5" :class="statusDot" />
            </span>
            <span>{{ statusText }}</span>
          </div>

          <span v-if="connectionStore.username" class="text-sm text-gray-600">
            {{ connectionStore.username }}
          </span>

          <router-link to="/settings" class="text-gray-400 hover:text-gray-600">
            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
          </router-link>

          <button
            v-if="connectionStore.isAbsConnected"
            @click="connectionStore.logout(); router.push('/setup')"
            class="text-sm text-gray-400 hover:text-red-500"
          >
            Logout
          </button>
        </div>
      </div>
    </div>
  </nav>
</template>
