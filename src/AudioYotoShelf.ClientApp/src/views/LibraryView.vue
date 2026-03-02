<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useLibraryStore, SORT_OPTIONS } from '@/stores/libraryStore'
import { useConnectionStore } from '@/stores/connectionStore'
import { useSelection } from '@/composables/useSelection'
import { useToast } from '@/composables/useToast'
import { useConfirm } from '@/composables/useConfirm'
import { transferApi } from '@/services/api'

const router = useRouter()
const libraryStore = useLibraryStore()
const connectionStore = useConnectionStore()
const toast = useToast()
const { confirm } = useConfirm()
const {
  isSelectionMode, selectionCount, hasSelection,
  toggleSelect, isSelected, selectAll, clearSelection,
  enterSelectionMode, getSelectedArray,
} = useSelection()

const isBatchTransferring = ref(false)

onMounted(async () => {
  await libraryStore.loadLibraries()
  if (libraryStore.selectedLibraryId) {
    await libraryStore.loadItems()
  }
})

watch(() => libraryStore.selectedLibraryId, async (newId) => {
  if (newId) {
    clearSelection()
    if (libraryStore.viewMode === 'books') {
      await libraryStore.loadItems()
    } else {
      await libraryStore.loadSeries()
    }
  }
})

watch(() => libraryStore.viewMode, async (mode) => {
  clearSelection()
  if (mode === 'books') await libraryStore.loadItems()
  else await libraryStore.loadSeries()
})

function formatDuration(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

function handleBookClick(itemId: string) {
  if (isSelectionMode.value) {
    toggleSelect(itemId)
  } else {
    router.push({ name: 'book-detail', params: { itemId } })
  }
}

function handleBookLongPress(itemId: string) {
  enterSelectionMode()
  toggleSelect(itemId)
}

function handleSelectAll() {
  selectAll(libraryStore.items.map(i => i.id))
}

async function handleBatchTransfer() {
  const ids = getSelectedArray()
  if (ids.length === 0) return

  const ok = await confirm({
    title: `Transfer ${ids.length} book${ids.length > 1 ? 's' : ''} to Yoto?`,
    message: `This will create ${ids.length} Yoto MYO card${ids.length > 1 ? 's' : ''}. Each book will be queued as a separate transfer.`,
    confirmText: 'Transfer All',
    variant: 'primary',
  })
  if (!ok) return

  isBatchTransferring.value = true
  try {
    const ucid = connectionStore.userConnectionId!
    const { data } = await transferApi.transferBatch(ucid, {
      absLibraryItemIds: ids,
    })
    toast.success(`Queued ${data.queued} transfer${data.queued > 1 ? 's' : ''}`)
    clearSelection()
  } catch {
    toast.error('Failed to queue batch transfer')
  } finally {
    isBatchTransferring.value = false
  }
}

function openSeries(seriesId: string) {
  router.push({ name: 'series-detail', params: { seriesId } })
}

function onCoverError(e: Event) {
  const img = e.target as HTMLImageElement
  img.src = 'data:image/svg+xml,' + encodeURIComponent(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 300" fill="none">
      <rect width="200" height="300" rx="8" fill="#E5E7EB"/>
      <path d="M80 120h40v8H80v-8zm-8 24h56v6H72v-6zm0 16h56v6H72v-6zm0 16h40v6H72v-6z" fill="#9CA3AF"/>
      <rect x="76" y="80" width="48" height="24" rx="4" fill="#9CA3AF" opacity="0.5"/>
    </svg>
  `)
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header row -->
    <div class="flex items-center justify-between flex-wrap gap-3">
      <h1 class="text-2xl font-bold text-gray-900">Library</h1>

      <div class="flex items-center space-x-3">
        <!-- Library selector -->
        <select
          v-model="libraryStore.selectedLibraryId"
          @change="libraryStore.selectLibrary(($event.target as HTMLSelectElement).value)"
          class="input-field w-auto"
        >
          <option v-for="lib in libraryStore.libraries" :key="lib.id" :value="lib.id">
            {{ lib.name }}
          </option>
        </select>

        <!-- View mode toggle -->
        <div class="flex bg-gray-100 rounded-lg p-1">
          <button
            @click="libraryStore.viewMode = 'books'"
            class="px-3 py-1 rounded-md text-sm font-medium transition-colors"
            :class="libraryStore.viewMode === 'books' ? 'bg-white shadow-sm text-yoto-blue' : 'text-gray-500'"
          >
            Books
          </button>
          <button
            @click="libraryStore.viewMode = 'series'"
            class="px-3 py-1 rounded-md text-sm font-medium transition-colors"
            :class="libraryStore.viewMode === 'series' ? 'bg-white shadow-sm text-yoto-blue' : 'text-gray-500'"
          >
            Series
          </button>
        </div>
      </div>
    </div>

    <!-- Search & Sort bar (books mode only) -->
    <div v-if="libraryStore.viewMode === 'books'" class="flex items-center gap-3 flex-wrap">
      <!-- Search input -->
      <div class="relative flex-1 min-w-[200px] max-w-md">
        <svg class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 pointer-events-none" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
        </svg>
        <input
          v-model="libraryStore.searchQuery"
          type="text"
          placeholder="Search by title, author..."
          class="input-field pl-10 pr-8 w-full"
        />
        <button
          v-if="libraryStore.searchQuery"
          @click="libraryStore.clearSearch()"
          class="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-gray-400 hover:text-gray-600 transition-colors"
        >
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
            <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
          </svg>
        </button>
      </div>

      <!-- Sort dropdown + direction toggle -->
      <div class="flex items-center gap-2">
        <span class="text-sm text-gray-500 whitespace-nowrap">Sort by</span>
        <select v-model="libraryStore.sortField" class="input-field w-auto text-sm">
          <option v-for="opt in SORT_OPTIONS" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
        </select>
        <button
          @click="libraryStore.sortDesc = !libraryStore.sortDesc"
          class="p-2 rounded-md hover:bg-gray-100 transition-colors text-gray-500"
          :title="libraryStore.sortDesc ? 'Descending (click for ascending)' : 'Ascending (click for descending)'"
        >
          <svg v-if="!libraryStore.sortDesc" xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
            <path fill-rule="evenodd" d="M3.293 9.707a1 1 0 010-1.414l6-6a1 1 0 011.414 0l6 6a1 1 0 01-1.414 1.414L11 5.414V17a1 1 0 11-2 0V5.414L4.707 9.707a1 1 0 01-1.414 0z" clip-rule="evenodd" />
          </svg>
          <svg v-else xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
            <path fill-rule="evenodd" d="M16.707 10.293a1 1 0 010 1.414l-6 6a1 1 0 01-1.414 0l-6-6a1 1 0 111.414-1.414L9 14.586V3a1 1 0 012 0v11.586l4.293-4.293a1 1 0 011.414 0z" clip-rule="evenodd" />
          </svg>
        </button>
      </div>

      <!-- Selection mode toggle -->
      <button
        v-if="!isSelectionMode && libraryStore.items.length > 0"
        @click="enterSelectionMode()"
        class="btn-secondary text-sm"
        title="Select multiple books for batch transfer"
      >
        Select
      </button>

      <!-- Selection actions -->
      <div v-if="isSelectionMode" class="flex items-center gap-2">
        <button @click="handleSelectAll()" class="btn-secondary text-sm">Select All</button>
        <button @click="clearSelection()" class="btn-secondary text-sm">Cancel</button>
      </div>

      <!-- Results count -->
      <span v-if="!libraryStore.isLoading" class="text-sm text-gray-400 whitespace-nowrap ml-auto">
        {{ libraryStore.totalItems }} book{{ libraryStore.totalItems !== 1 ? 's' : '' }}
        <span v-if="libraryStore.searchQuery">matching "{{ libraryStore.searchQuery }}"</span>
      </span>
    </div>

    <!-- Loading -->
    <div v-if="libraryStore.isLoading" class="text-center py-16">
      <div class="inline-block h-8 w-8 animate-spin rounded-full border-4 border-yoto-blue border-r-transparent" />
      <p class="mt-3 text-sm text-gray-400">Loading library...</p>
    </div>

    <!-- Empty state: No books -->
    <div v-else-if="libraryStore.viewMode === 'books' && libraryStore.hasNoResults" class="text-center py-16">
      <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-16 w-16 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25" />
      </svg>
      <h3 class="mt-4 text-lg font-medium text-gray-700">
        {{ libraryStore.searchQuery ? 'No books match your search' : 'No books in this library' }}
      </h3>
      <p class="mt-2 text-sm text-gray-400">
        {{ libraryStore.searchQuery ? 'Try a different search term or clear the filter.' : 'Add some audiobooks to Audiobookshelf to get started.' }}
      </p>
      <button v-if="libraryStore.searchQuery" @click="libraryStore.clearSearch()" class="mt-4 btn-secondary text-sm">
        Clear search
      </button>
    </div>

    <!-- Books Grid -->
    <div v-else-if="libraryStore.viewMode === 'books'" class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4">
      <div
        v-for="item in libraryStore.items"
        :key="item.id"
        @click="handleBookClick(item.id)"
        @contextmenu.prevent="handleBookLongPress(item.id)"
        class="card cursor-pointer hover:shadow-md hover:-translate-y-0.5 transition-all p-0 overflow-hidden relative"
        :class="{ 'ring-2 ring-yoto-blue': isSelectionMode && isSelected(item.id) }"
      >
        <!-- Selection checkbox -->
        <div
          v-if="isSelectionMode"
          class="absolute top-2 left-2 z-10 w-6 h-6 rounded-full border-2 flex items-center justify-center transition-colors"
          :class="isSelected(item.id) ? 'bg-yoto-blue border-yoto-blue' : 'bg-white/80 border-gray-300'"
        >
          <svg v-if="isSelected(item.id)" xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 text-white" viewBox="0 0 20 20" fill="currentColor">
            <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd" />
          </svg>
        </div>

        <img
          :src="libraryStore.getCoverUrl(item.id)"
          :alt="item.media?.metadata?.title ?? 'Cover'"
          class="w-full aspect-[2/3] object-cover bg-gray-100"
          loading="lazy"
          @error="onCoverError"
        />
        <div class="p-3">
          <h3 class="font-medium text-sm text-gray-900 line-clamp-2">
            {{ item.media?.metadata?.title ?? 'Unknown' }}
          </h3>
          <p class="text-xs text-gray-500 mt-1 line-clamp-1">
            {{ item.media?.metadata?.authors?.map(a => a.name).join(', ') }}
          </p>
          <p v-if="item.media" class="text-xs text-gray-400 mt-1">
            {{ formatDuration(item.media.duration) }} · {{ item.media.numChapters }} ch.
          </p>
        </div>
      </div>
    </div>

    <!-- Empty state: No series -->
    <div v-else-if="libraryStore.viewMode === 'series' && libraryStore.series.length === 0 && !libraryStore.isLoading" class="text-center py-16">
      <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-16 w-16 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" d="M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M5.625 4.5h12.75a1.875 1.875 0 010 3.75H5.625a1.875 1.875 0 010-3.75z" />
      </svg>
      <h3 class="mt-4 text-lg font-medium text-gray-700">No series found</h3>
      <p class="mt-2 text-sm text-gray-400">Series are detected from your audiobook metadata in Audiobookshelf.</p>
    </div>

    <!-- Series List -->
    <div v-else-if="libraryStore.viewMode === 'series'" class="space-y-3">
      <div
        v-for="s in libraryStore.series"
        :key="s.id"
        @click="openSeries(s.id)"
        class="card cursor-pointer hover:shadow-md transition-shadow flex items-center space-x-4"
      >
        <div class="flex -space-x-3 shrink-0">
          <img
            v-for="(book, i) in s.books.slice(0, 3)"
            :key="book.id"
            :src="libraryStore.getCoverUrl(book.id)"
            class="w-12 h-16 object-cover rounded border-2 border-white"
            :style="{ zIndex: 3 - i }"
            loading="lazy"
            @error="onCoverError"
          />
        </div>
        <div class="flex-1 min-w-0">
          <h3 class="font-semibold text-gray-900">{{ s.name }}</h3>
          <p class="text-sm text-gray-500">{{ s.books.length }} book{{ s.books.length !== 1 ? 's' : '' }} · {{ formatDuration(s.totalDuration) }}</p>
        </div>
        <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5 text-gray-400 shrink-0" viewBox="0 0 20 20" fill="currentColor">
          <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd" />
        </svg>
      </div>
    </div>

    <!-- Pagination -->
    <div v-if="!libraryStore.isLoading && libraryStore.viewMode === 'books' && libraryStore.totalItems > libraryStore.pageSize" class="flex justify-center items-center space-x-2 pt-4">
      <button
        @click="libraryStore.loadItems(libraryStore.currentPage - 1)"
        :disabled="libraryStore.currentPage === 0"
        class="btn-secondary text-sm"
      >Previous</button>
      <span class="px-4 py-2 text-sm text-gray-500">
        Page {{ libraryStore.currentPage + 1 }} of {{ libraryStore.pageCount }}
      </span>
      <button
        @click="libraryStore.loadItems(libraryStore.currentPage + 1)"
        :disabled="(libraryStore.currentPage + 1) * libraryStore.pageSize >= libraryStore.totalItems"
        class="btn-secondary text-sm"
      >Next</button>
      <select v-model.number="libraryStore.pageSize" class="input-field w-auto text-sm ml-2">
        <option :value="20">20 / page</option>
        <option :value="40">40 / page</option>
        <option :value="60">60 / page</option>
        <option :value="100">100 / page</option>
      </select>
    </div>

    <!-- Floating batch action bar (Phase 2) -->
    <Transition
      enter-active-class="transition-all duration-200 ease-out"
      leave-active-class="transition-all duration-150 ease-in"
      enter-from-class="translate-y-full opacity-0"
      leave-to-class="translate-y-full opacity-0"
    >
      <div
        v-if="hasSelection"
        class="fixed bottom-6 left-1/2 -translate-x-1/2 bg-white rounded-xl shadow-xl border border-gray-200 px-6 py-3 flex items-center gap-4 z-40"
      >
        <span class="text-sm font-medium text-gray-700">
          {{ selectionCount }} selected
        </span>
        <button
          @click="handleBatchTransfer"
          :disabled="isBatchTransferring"
          class="btn-primary text-sm"
        >
          {{ isBatchTransferring ? 'Queuing...' : `Transfer ${selectionCount} to Yoto` }}
        </button>
        <button @click="clearSelection()" class="text-sm text-gray-500 hover:text-gray-700">
          Cancel
        </button>
      </div>
    </Transition>
  </div>
</template>