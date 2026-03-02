<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useConnectionStore } from '@/stores/connectionStore'
import AppNav from '@/components/common/AppNav.vue'
import ToastContainer from '@/components/common/ToastContainer.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'

const router = useRouter()
const connectionStore = useConnectionStore()

onMounted(async () => {
  if (connectionStore.userConnectionId) {
    await connectionStore.loadStatus()
    if (!connectionStore.isAbsConnected) {
      router.push('/setup')
    }
  } else {
    router.push('/setup')
  }
})
</script>

<template>
  <div class="min-h-screen bg-gray-50">
    <AppNav v-if="connectionStore.isAbsConnected" />
    <main class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
      <router-view />
    </main>
    <ToastContainer />
    <ConfirmDialog />
  </div>
</template>
