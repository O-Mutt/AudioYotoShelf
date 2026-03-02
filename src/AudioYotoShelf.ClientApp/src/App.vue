<script setup lang="ts">
import { onMounted } from 'vue'
import { useConnectionStore } from '@/stores/connectionStore'
import { useSignalR } from '@/composables/useSignalR'
import AppNav from '@/components/common/AppNav.vue'

const connectionStore = useConnectionStore()
const { connect: connectSignalR } = useSignalR()

onMounted(async () => {
  if (connectionStore.userConnectionId) {
    await connectionStore.refreshStatus()
  }
  await connectSignalR()
})
</script>

<template>
  <div class="min-h-screen bg-gray-50">
    <AppNav />
    <main class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <router-view />
    </main>
  </div>
</template>
