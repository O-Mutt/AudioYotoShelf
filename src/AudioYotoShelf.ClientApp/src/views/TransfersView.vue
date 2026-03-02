<script setup lang="ts">
import { onMounted, ref, computed, watch } from 'vue'
import { useConnectionStore } from '@/stores/connectionStore'
import { useSignalR } from '@/composables/useSignalR'
import { useToast } from '@/composables/useToast'
import { useConfirm } from '@/composables/useConfirm'
import { transferApi } from '@/services/api'
import type { TransferResponse, TransferStatus } from '@/types'

const connectionStore = useConnectionStore()
const { progressUpdates, joinTransfer } = useSignalR()
const toast = useToast()
const { confirm } = useConfirm()

const transfers = ref<TransferResponse[]>([])
const totalTransfers = ref(0)
const currentPage = ref(0)
const isLoading = ref(true)
const filterStatus = ref<TransferStatus | ''>('')

const activeTransfers = computed(() =>
  transfers.value.filter(t =>
    !['Completed', 'Failed', 'Cancelled'].includes(t.status)
  )
)

onMounted(async () => {
  await loadTransfers()
  for (const t of activeTransfers.value) {
    await joinTransfer(t.id)
  }
})

watch(progressUpdates, (updates) => {
  for (const [id, update] of updates) {
    const transfer = transfers.value.find(t => t.id === id)
    if (transfer) {
      transfer.status = update.status
      transfer.progressPercent = update.progressPercent
      transfer.errorMessage = update.errorMessage
    }
  }
}, { deep: true })

async function loadTransfers(page = 0) {
  if (!connectionStore.userConnectionId) return
  isLoading.value = true
  try {
    const { data } = await transferApi.getTransfers(
      connectionStore.userConnectionId, page, 20,
      filterStatus.value || undefined
    )
    transfers.value = data.results
    totalTransfers.value = data.total
    currentPage.value = page
  } finally {
    isLoading.value = false
  }
}

async function handleRetry(transferId: string, title: string) {
  try {
    await transferApi.retryTransfer(transferId)
    toast.success(`Retry queued for "${title}"`)
    await loadTransfers(currentPage.value)
  } catch {
    toast.error('Failed to queue retry')
  }
}

async function handleCancel(transferId: string, title: string) {
  const ok = await confirm({
    title: 'Cancel Transfer',
    message: `Cancel the transfer for "${title}"? Any uploaded tracks will remain on Yoto.`,
    confirmText: 'Cancel Transfer',
    variant: 'danger',
  })
  if (!ok) return

  try {
    await transferApi.cancelTransfer(transferId)
    toast.info(`Transfer cancelled: "${title}"`)
    await loadTransfers(currentPage.value)
  } catch {
    toast.error('Failed to cancel transfer')
  }
}

function statusColor(status: TransferStatus): string {
  const map: Record<TransferStatus, string> = {
    Pending: 'bg-gray-100 text-gray-600',
    DownloadingAudio: 'bg-blue-100 text-blue-700',
    UploadingToYoto: 'bg-blue-100 text-blue-700',
    AwaitingTranscode: 'bg-yellow-100 text-yellow-700',
    GeneratingIcons: 'bg-purple-100 text-purple-700',
    CreatingCard: 'bg-indigo-100 text-indigo-700',
    Completed: 'bg-green-100 text-green-700',
    Failed: 'bg-red-100 text-red-700',
    Cancelled: 'bg-gray-100 text-gray-500',
  }
  return map[status] ?? 'bg-gray-100 text-gray-600'
}

function isActive(status: TransferStatus): boolean {
  return !['Completed', 'Failed', 'Cancelled'].includes(status)
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
  })
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between">
      <h1 class="text-2xl font-bold text-gray-900">Transfers</h1>
      <select v-model="filterStatus" @change="loadTransfers(0)" class="input-field w-auto">
        <option value="">All Statuses</option>
        <option value="Pending">Pending</option>
        <option value="DownloadingAudio">Downloading</option>
        <option value="UploadingToYoto">Uploading</option>
        <option value="AwaitingTranscode">Transcoding</option>
        <option value="Completed">Completed</option>
        <option value="Failed">Failed</option>
      </select>
    </div>

    <div v-if="isLoading" class="text-center py-12 text-gray-400">Loading transfers...</div>

    <div v-else-if="transfers.length === 0" class="text-center py-12">
      <p class="text-gray-400">No transfers yet. Browse your library to start transferring books to Yoto cards.</p>
    </div>

    <div v-else class="space-y-3">
      <div
        v-for="transfer in transfers"
        :key="transfer.id"
        class="card p-4"
      >
        <div class="flex items-start justify-between">
          <div class="flex-1 min-w-0">
            <div class="flex items-center space-x-3">
              <h3 class="font-medium text-gray-900 truncate">{{ transfer.bookTitle }}</h3>
              <span
                class="px-2 py-0.5 rounded-full text-xs font-medium flex-shrink-0"
                :class="statusColor(transfer.status)"
              >
                {{ transfer.status }}
              </span>
            </div>
            <p v-if="transfer.bookAuthor" class="text-sm text-gray-500 mt-1">{{ transfer.bookAuthor }}</p>
            <div class="flex items-center space-x-4 text-xs text-gray-400 mt-2">
              <span>{{ formatDate(transfer.createdAt) }}</span>
              <span v-if="transfer.seriesName" class="text-yoto-blue">
                {{ transfer.seriesName }} {{ transfer.seriesSequence ? `#${transfer.seriesSequence}` : '' }}
              </span>
              <span>Age: {{ transfer.ageRange.effectiveMin }}–{{ transfer.ageRange.effectiveMax }}</span>
              <span>{{ transfer.tracks.length }} tracks</span>
            </div>
          </div>

          <div class="text-right flex-shrink-0 ml-4">
            <span v-if="transfer.yotoCardId" class="text-xs text-gray-400 font-mono">
              {{ transfer.yotoCardId.slice(0, 8) }}...
            </span>
          </div>
        </div>

        <!-- Progress bar for active transfers -->
        <div
          v-if="!['Completed', 'Failed', 'Cancelled'].includes(transfer.status)"
          class="mt-3"
        >
          <div class="w-full bg-gray-100 rounded-full h-2">
            <div
              class="bg-yoto-blue h-2 rounded-full transition-all duration-300"
              :style="{ width: `${transfer.progressPercent}%` }"
            />
          </div>
          <p class="text-xs text-gray-400 mt-1">{{ transfer.progressPercent }}%</p>
        </div>

        <!-- Error message -->
        <p v-if="transfer.errorMessage" class="mt-2 text-sm text-red-600 bg-red-50 rounded p-2">
          {{ transfer.errorMessage }}
        </p>

        <!-- Action buttons (Phase 6) -->
        <div class="mt-3 flex gap-2">
          <button
            v-if="transfer.status === 'Failed'"
            @click="handleRetry(transfer.id, transfer.bookTitle)"
            class="text-sm text-yoto-blue hover:text-blue-700 font-medium"
          >
            Retry
          </button>
          <button
            v-if="isActive(transfer.status)"
            @click="handleCancel(transfer.id, transfer.bookTitle)"
            class="text-sm text-red-600 hover:text-red-700 font-medium"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>

    <!-- Pagination -->
    <div v-if="totalTransfers > 20" class="flex justify-center space-x-2 pt-4">
      <button
        @click="loadTransfers(currentPage - 1)"
        :disabled="currentPage === 0"
        class="btn-secondary text-sm"
      >
        Previous
      </button>
      <span class="px-4 py-2 text-sm text-gray-500">
        Page {{ currentPage + 1 }} of {{ Math.ceil(totalTransfers / 20) }}
      </span>
      <button
        @click="loadTransfers(currentPage + 1)"
        :disabled="(currentPage + 1) * 20 >= totalTransfers"
        class="btn-secondary text-sm"
      >
        Next
      </button>
    </div>
  </div>
</template>
