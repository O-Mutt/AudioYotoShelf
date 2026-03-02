<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useLibraryStore } from '@/stores/libraryStore'
import { useConnectionStore } from '@/stores/connectionStore'
import { libraryApi, transferApi } from '@/services/api'
import type { AbsSeriesItem } from '@/types'

const props = defineProps<{ seriesId: string }>()
const router = useRouter()
const libraryStore = useLibraryStore()
const connectionStore = useConnectionStore()

const seriesDetail = ref<AbsSeriesItem | null>(null)
const isLoading = ref(true)
const isTransferring = ref(false)
const transferMessage = ref<string | null>(null)

onMounted(async () => {
  if (!connectionStore.userConnectionId) return
  try {
    const { data } = await libraryApi.getSeriesDetail(connectionStore.userConnectionId, props.seriesId)
    seriesDetail.value = data as AbsSeriesItem
  } finally {
    isLoading.value = false
  }
})

function formatDuration(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

function openBook(bookId: string) {
  router.push({ name: 'book-detail', params: { itemId: bookId } })
}

async function transferSeries() {
  if (!connectionStore.userConnectionId || !libraryStore.selectedLibraryId) return
  isTransferring.value = true
  transferMessage.value = null
  try {
    await transferApi.transferSeries(connectionStore.userConnectionId, {
      absSeriesId: props.seriesId,
      absLibraryId: libraryStore.selectedLibraryId,
    })
    transferMessage.value = 'Series transfer queued successfully!'
  } catch (err: any) {
    transferMessage.value = `Error: ${err.response?.data?.message ?? err.message}`
  } finally {
    isTransferring.value = false
  }
}
</script>

<template>
  <div v-if="isLoading" class="text-center py-12 text-gray-400">Loading series...</div>

  <div v-else-if="seriesDetail" class="space-y-6">
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-3xl font-bold text-gray-900">{{ seriesDetail.name }}</h1>
        <p class="text-gray-500 mt-1">
          {{ seriesDetail.books.length }} books · {{ formatDuration(seriesDetail.totalDuration) }}
        </p>
      </div>
      <button
        @click="transferSeries"
        :disabled="isTransferring || !connectionStore.isYotoConnected"
        class="btn-primary"
      >
        {{ isTransferring ? 'Queueing...' : 'Transfer Entire Series' }}
      </button>
    </div>

    <p v-if="transferMessage" class="text-sm" :class="transferMessage.startsWith('Error') ? 'text-red-600' : 'text-green-600'">
      {{ transferMessage }}
    </p>

    <p v-if="seriesDetail.description" class="text-gray-600 text-sm">{{ seriesDetail.description }}</p>

    <!-- Books in series -->
    <div class="space-y-3">
      <div
        v-for="book in seriesDetail.books"
        :key="book.id"
        @click="openBook(book.id)"
        class="card cursor-pointer hover:shadow-md transition-shadow flex items-center space-x-4 p-4"
      >
        <img
          :src="libraryStore.getCoverUrl(book.id)"
          :alt="book.media?.metadata?.title ?? 'Cover'"
          class="w-14 h-20 object-cover rounded-lg"
          loading="lazy"
        />
        <div class="flex-1 min-w-0">
          <div class="flex items-center space-x-2">
            <span
              v-if="book.sequence"
              class="px-2 py-0.5 bg-yoto-blue/10 text-yoto-blue text-xs font-bold rounded-full"
            >
              #{{ book.sequence }}
            </span>
            <h3 class="font-medium text-gray-900 truncate">
              {{ book.media?.metadata?.title ?? 'Unknown' }}
            </h3>
          </div>
          <p class="text-sm text-gray-500 mt-1">
            {{ book.media?.metadata?.authors?.map(a => a.name).join(', ') }}
          </p>
          <p v-if="book.media" class="text-xs text-gray-400 mt-1">
            {{ formatDuration(book.media.duration) }} · {{ book.media.numChapters }} chapters
          </p>
        </div>
        <svg class="w-5 h-5 text-gray-300 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
        </svg>
      </div>
    </div>
  </div>
</template>
