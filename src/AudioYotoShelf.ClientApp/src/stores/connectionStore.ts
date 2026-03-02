import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/services/api'
import type { ConnectionStatus } from '@/types'

const STORAGE_KEY = 'ays_user_connection_id'

export const useConnectionStore = defineStore('connection', () => {
  const userConnectionId = ref<string | null>(localStorage.getItem(STORAGE_KEY))
  const status = ref<ConnectionStatus | null>(null)
  const isLoading = ref(false)

  const isAbsConnected = computed(() => status.value?.absConnected ?? false)
  const isYotoConnected = computed(() => status.value?.yotoConnected ?? false)
  const isFullyConnected = computed(() => isAbsConnected.value && isYotoConnected.value)
  const username = computed(() => status.value?.username ?? null)

  function setUserConnectionId(id: string) {
    userConnectionId.value = id
    localStorage.setItem(STORAGE_KEY, id)
  }

  async function loadStatus() {
    if (!userConnectionId.value) return
    isLoading.value = true
    try {
      const { data } = await authApi.getConnectionStatus(userConnectionId.value)
      status.value = data
    } catch {
      status.value = null
    } finally {
      isLoading.value = false
    }
  }

  // Phase 3: Save settings and refresh status
  async function updateSettings(settings: {
    defaultLibraryId?: string
    defaultMinAge?: number
    defaultMaxAge?: number
  }) {
    if (!userConnectionId.value) throw new Error('Not connected')
    const { data } = await authApi.updateSettings(userConnectionId.value, settings)
    status.value = data
  }

  function logout() {
    userConnectionId.value = null
    status.value = null
    localStorage.removeItem(STORAGE_KEY)
  }

  return {
    userConnectionId,
    status,
    isLoading,
    isAbsConnected,
    isYotoConnected,
    isFullyConnected,
    username,
    setUserConnectionId,
    loadStatus,
    refreshStatus: loadStatus,
    updateSettings,
    logout,
  }
})
