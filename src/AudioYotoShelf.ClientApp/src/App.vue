<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useConnectionStore } from '@/stores/connectionStore'
import { useToast } from '@/composables/useToast'
import AppNav from '@/components/common/AppNav.vue'
import ToastContainer from '@/components/common/ToastContainer.vue'
import ConfirmDialog from '@/components/common/ConfirmDialog.vue'
import ErrorBoundary from '@/components/common/ErrorBoundary.vue'
import api from '@/services/api'

const router = useRouter()
const connectionStore = useConnectionStore()
const toast = useToast()

// Phase 7: Global Axios error interceptor
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      connectionStore.logout()
      router.push('/setup')
      toast.error('Session expired. Please reconnect.')
    } else if (error.response?.status >= 500) {
      toast.error('Server error. Please try again later.')
    }
    return Promise.reject(error)
  }
)

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
      <ErrorBoundary>
        <router-view />
      </ErrorBoundary>
    </main>
    <ToastContainer />
    <ConfirmDialog />
  </div>
</template>
