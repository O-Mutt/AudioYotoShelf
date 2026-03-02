<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useConnectionStore } from '@/stores/connectionStore'

const router = useRouter()
const connectionStore = useConnectionStore()

// ABS form
const absUrl = ref('http://localhost:13378')
const absUsername = ref('')
const absPassword = ref('')

async function connectAbs() {
  await connectionStore.connectToAbs(absUrl.value, absUsername.value, absPassword.value)
}

async function startYotoAuth() {
  await connectionStore.startYotoAuth()
}

function goToLibrary() {
  router.push('/library')
}
</script>

<template>
  <div class="max-w-lg mx-auto space-y-8">
    <div class="text-center">
      <h1 class="text-3xl font-bold text-gray-900">Welcome to AudioYotoShelf</h1>
      <p class="mt-2 text-gray-500">Connect your Audiobookshelf and Yoto accounts to get started.</p>
    </div>

    <!-- Step 1: Audiobookshelf -->
    <div class="card">
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-lg font-semibold">1. Connect Audiobookshelf</h2>
        <span
          v-if="connectionStore.isAbsConnected"
          class="text-sm text-green-600 font-medium"
        >
          Connected
        </span>
      </div>

      <form v-if="!connectionStore.isAbsConnected" @submit.prevent="connectAbs" class="space-y-4">
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Server URL</label>
          <input v-model="absUrl" type="url" class="input-field" placeholder="http://localhost:13378" required />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Username</label>
          <input v-model="absUsername" type="text" class="input-field" required />
        </div>
        <div>
          <label class="block text-sm font-medium text-gray-700 mb-1">Password</label>
          <input v-model="absPassword" type="password" class="input-field" required />
        </div>
        <button type="submit" class="btn-primary w-full" :disabled="connectionStore.isLoading">
          {{ connectionStore.isLoading ? 'Connecting...' : 'Connect' }}
        </button>
      </form>

      <div v-else class="text-sm text-gray-600">
        Connected as <strong>{{ connectionStore.username }}</strong>
        to {{ connectionStore.status?.audiobookshelfUrl }}
      </div>

      <p v-if="connectionStore.error" class="mt-2 text-sm text-red-600">
        {{ connectionStore.error }}
      </p>
    </div>

    <!-- Step 2: Yoto -->
    <div class="card" :class="{ 'opacity-50': !connectionStore.isAbsConnected }">
      <div class="flex items-center justify-between mb-4">
        <h2 class="text-lg font-semibold">2. Connect Yoto</h2>
        <span
          v-if="connectionStore.isYotoConnected"
          class="text-sm text-green-600 font-medium"
        >
          Connected
        </span>
      </div>

      <div v-if="!connectionStore.isAbsConnected" class="text-sm text-gray-400">
        Connect to Audiobookshelf first.
      </div>

      <div v-else-if="connectionStore.isYotoConnected" class="text-sm text-gray-600">
        Yoto account connected and authorized.
      </div>

      <div v-else>
        <p class="text-sm text-gray-600 mb-4">
          Authorize AudioYotoShelf to manage your Yoto MYO cards.
        </p>
        <button
          @click="startYotoAuth"
          class="btn-primary w-full"
          :disabled="!connectionStore.isAbsConnected || connectionStore.isLoading"
        >
          Authorize with Yoto
        </button>
      </div>
    </div>

    <!-- Continue button -->
    <button
      v-if="connectionStore.isAbsConnected"
      @click="goToLibrary"
      class="btn-primary w-full text-lg py-3"
    >
      {{ connectionStore.isYotoConnected ? 'Go to Library' : 'Continue (Yoto optional)' }}
    </button>
  </div>
</template>
