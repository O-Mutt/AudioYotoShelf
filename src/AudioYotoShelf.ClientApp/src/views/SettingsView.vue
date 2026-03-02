<script setup lang="ts">
import { useConnectionStore } from '@/stores/connectionStore'
import { useRouter } from 'vue-router'

const connectionStore = useConnectionStore()
const router = useRouter()

function goToSetup() {
  router.push('/setup')
}
</script>

<template>
  <div class="max-w-2xl mx-auto space-y-6">
    <h1 class="text-2xl font-bold text-gray-900">Settings</h1>

    <!-- Connection Status -->
    <div class="card">
      <h2 class="text-lg font-semibold mb-4">Connections</h2>
      <div class="space-y-3">
        <div class="flex items-center justify-between">
          <div>
            <p class="font-medium text-gray-700">Audiobookshelf</p>
            <p class="text-sm text-gray-400">
              {{ connectionStore.isAbsConnected
                ? connectionStore.status?.audiobookshelfUrl
                : 'Not connected' }}
            </p>
          </div>
          <span
            class="px-3 py-1 rounded-full text-sm font-medium"
            :class="connectionStore.isAbsConnected ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'"
          >
            {{ connectionStore.isAbsConnected ? 'Connected' : 'Disconnected' }}
          </span>
        </div>

        <div class="flex items-center justify-between">
          <div>
            <p class="font-medium text-gray-700">Yoto</p>
            <p class="text-sm text-gray-400">
              {{ connectionStore.isYotoConnected ? 'OAuth authorized' : 'Not connected' }}
            </p>
          </div>
          <span
            class="px-3 py-1 rounded-full text-sm font-medium"
            :class="connectionStore.isYotoConnected ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'"
          >
            {{ connectionStore.isYotoConnected ? 'Connected' : 'Disconnected' }}
          </span>
        </div>
      </div>

      <button @click="goToSetup" class="btn-secondary mt-4">
        Manage Connections
      </button>
    </div>

    <!-- Default Age Range -->
    <div class="card">
      <h2 class="text-lg font-semibold mb-4">Default Age Range</h2>
      <p class="text-sm text-gray-500 mb-4">
        Default age range used when no metadata signals are found.
      </p>
      <div class="grid grid-cols-2 gap-6">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">
            Min Age: {{ connectionStore.status?.defaultMinAge ?? 5 }}
          </label>
          <input type="range" min="0" max="18" :value="connectionStore.status?.defaultMinAge ?? 5" class="w-full accent-yoto-blue" disabled />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">
            Max Age: {{ connectionStore.status?.defaultMaxAge ?? 10 }}
          </label>
          <input type="range" min="0" max="18" :value="connectionStore.status?.defaultMaxAge ?? 10" class="w-full accent-yoto-blue" disabled />
        </div>
      </div>
    </div>

    <!-- About -->
    <div class="card">
      <h2 class="text-lg font-semibold mb-2">About AudioYotoShelf</h2>
      <p class="text-sm text-gray-500">
        Bridge your Audiobookshelf library to Yoto MYO cards. Transfer audiobooks with
        auto-generated pixel art chapter icons.
      </p>
      <div class="mt-3 text-xs text-gray-400 space-y-1">
        <p>Version 0.1.0</p>
        <p>
          <a href="/hangfire" target="_blank" class="text-yoto-blue hover:underline">
            Hangfire Dashboard (Background Jobs)
          </a>
        </p>
      </div>
    </div>

    <!-- Danger Zone -->
    <div class="card border-red-200">
      <h2 class="text-lg font-semibold text-red-600 mb-4">Danger Zone</h2>
      <button
        @click="connectionStore.logout(); router.push('/setup')"
        class="btn-danger"
      >
        Disconnect &amp; Logout
      </button>
    </div>
  </div>
</template>
