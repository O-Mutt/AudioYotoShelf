import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/services/api'
import type { ConnectionStatus } from '@/types'

const STORAGE_KEY = 'ays_user_connection_id'

export const useConnectionStore = defineStore('connection', () => {
  const userConnectionId = ref<string | null>(localStorage.getItem(STORAGE_KEY))
  const status = ref<ConnectionStatus | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

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

  /** Connect to Audiobookshelf with URL + credentials */
  async function connectToAbs(baseUrl: string, absUsername: string, password: string) {
    isLoading.value = true
    error.value = null
    try {
      const { data } = await authApi.connectAbs(baseUrl, absUsername, password)
      setUserConnectionId(data.userConnectionId)
      // Reload full status so all fields are populated
      await loadStatus()
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to connect to Audiobookshelf'
      error.value = msg
    } finally {
      isLoading.value = false
    }
  }

  /** Start Yoto OAuth authorization code flow (full page redirect) */
  async function startYotoAuth() {
    if (!userConnectionId.value) return
    isLoading.value = true
    error.value = null
    try {
      const { data } = await authApi.getYotoAuthUrl(userConnectionId.value)
      window.location.href = data.authUrl
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to start Yoto authorization'
      error.value = msg
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
    error.value = null
    localStorage.removeItem(STORAGE_KEY)
  }

  return {
    userConnectionId,
    status,
    isLoading,
    error,
    isAbsConnected,
    isYotoConnected,
    isFullyConnected,
    username,
    setUserConnectionId,
    loadStatus,
    refreshStatus: loadStatus,
    connectToAbs,
    startYotoAuth,
    updateSettings,
    logout,
  }
})