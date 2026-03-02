import { ref, onUnmounted } from 'vue'
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import type { TransferProgressUpdate } from '@/types'

const connection = ref<HubConnection | null>(null)
const isConnected = ref(false)

export function useSignalR() {
  const progressUpdates = ref<Map<string, TransferProgressUpdate>>(new Map())

  async function connect() {
    if (connection.value?.state === 'Connected') return

    connection.value = new HubConnectionBuilder()
      .withUrl('/hubs/transfer')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build()

    connection.value.on('TransferProgress', (update: TransferProgressUpdate) => {
      progressUpdates.value.set(update.transferId, update)
    })

    connection.value.onreconnected(() => {
      isConnected.value = true
      console.log('SignalR reconnected')
    })

    connection.value.onclose(() => {
      isConnected.value = false
    })

    try {
      await connection.value.start()
      isConnected.value = true
    } catch (err) {
      console.error('SignalR connection failed:', err)
      isConnected.value = false
    }
  }

  async function joinTransfer(transferId: string) {
    if (connection.value?.state === 'Connected') {
      await connection.value.invoke('JoinTransferGroup', transferId)
    }
  }

  async function leaveTransfer(transferId: string) {
    if (connection.value?.state === 'Connected') {
      await connection.value.invoke('LeaveTransferGroup', transferId)
    }
  }

  async function disconnect() {
    if (connection.value) {
      await connection.value.stop()
      isConnected.value = false
    }
  }

  onUnmounted(() => {
    // Don't disconnect on unmount — shared connection
  })

  return {
    connect,
    disconnect,
    joinTransfer,
    leaveTransfer,
    isConnected,
    progressUpdates,
  }
}
