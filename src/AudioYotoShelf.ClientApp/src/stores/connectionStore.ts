import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/services/api'
import type { ConnectionStatus } from '@/types'

export const useConnectionStore = defineStore('connection', () => {
  const userConnectionId = ref<string | null>(localStorage.getItem('userConnectionId'))
  const status = ref<ConnectionStatus | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Yoto device flow state
  const yotoAuthPending = ref(false)
  const yotoUserCode = ref<string | null>(null)
  const yotoVerificationUri = ref<string | null>(null)

  const isAbsConnected = computed(() => status.value?.absConnected ?? false)
  const isYotoConnected = computed(() => status.value?.yotoConnected ?? false)
  const isFullyConnected = computed(() => isAbsConnected.value && isYotoConnected.value)
  const username = computed(() => status.value?.username ?? '')

  async function connectToAbs(baseUrl: string, username: string, password: string) {
    isLoading.value = true
    error.value = null
    try {
      const { data } = await authApi.connectAbs(baseUrl, username, password)
      userConnectionId.value = data.userConnectionId
      localStorage.setItem('userConnectionId', data.userConnectionId)
      await refreshStatus()
    } catch (err: any) {
      error.value = err.response?.data?.message ?? 'Failed to connect to Audiobookshelf'
      throw err
    } finally {
      isLoading.value = false
    }
  }

  async function initiateYotoAuth() {
    if (!userConnectionId.value) throw new Error('Not connected to ABS first')
    isLoading.value = true
    error.value = null
    try {
      const { data } = await authApi.initiateYotoAuth(userConnectionId.value)
      yotoAuthPending.value = true
      yotoUserCode.value = data.userCode
      yotoVerificationUri.value = data.verificationUriComplete || data.verificationUri
      return data
    } catch (err: any) {
      error.value = err.response?.data?.message ?? 'Failed to initiate Yoto authorization'
      throw err
    } finally {
      isLoading.value = false
    }
  }

  async function pollYotoAuth(): Promise<boolean> {
    if (!userConnectionId.value) return false
    try {
      const { data } = await authApi.pollYotoAuth(userConnectionId.value)
      if (data.status === 'authorized') {
        yotoAuthPending.value = false
        yotoUserCode.value = null
        yotoVerificationUri.value = null
        await refreshStatus()
        return true
      }
      return false
    } catch {
      return false
    }
  }

  async function refreshStatus() {
    if (!userConnectionId.value) return
    try {
      const { data } = await authApi.getConnectionStatus(userConnectionId.value)
      status.value = data
    } catch (err: any) {
      if (err.response?.status === 404) {
        // Connection no longer exists
        logout()
      }
    }
  }

  function logout() {
    userConnectionId.value = null
    status.value = null
    yotoAuthPending.value = false
    yotoUserCode.value = null
    yotoVerificationUri.value = null
    localStorage.removeItem('userConnectionId')
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
    yotoAuthPending,
    yotoUserCode,
    yotoVerificationUri,
    connectToAbs,
    initiateYotoAuth,
    pollYotoAuth,
    refreshStatus,
    logout,
  }
})
