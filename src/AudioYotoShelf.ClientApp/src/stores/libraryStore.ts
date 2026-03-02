import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { libraryApi } from '@/services/api'
import { useConnectionStore } from './connectionStore'
import type { AbsLibrary, AbsLibraryItem, AbsSeriesItem, BookDetailResponse } from '@/types'

export type SortField = 'media.metadata.title' | 'media.metadata.authorName' | 'media.duration' | 'addedAt'

export const SORT_OPTIONS: { label: string; value: SortField }[] = [
  { label: 'Title', value: 'media.metadata.title' },
  { label: 'Author', value: 'media.metadata.authorName' },
  { label: 'Duration', value: 'media.duration' },
  { label: 'Recently Added', value: 'addedAt' },
]

export const useLibraryStore = defineStore('library', () => {
  const connectionStore = useConnectionStore()

  // Core state
  const libraries = ref<AbsLibrary[]>([])
  const selectedLibraryId = ref<string | null>(null)
  const items = ref<AbsLibraryItem[]>([])
  const totalItems = ref(0)
  const currentPage = ref(0)
  const series = ref<AbsSeriesItem[]>([])
  const totalSeries = ref(0)
  const currentBookDetail = ref<BookDetailResponse | null>(null)
  const isLoading = ref(false)
  const viewMode = ref<'books' | 'series'>('books')

  // Search, sort & pagination state
  const searchQuery = ref('')
  const sortField = ref<SortField>('media.metadata.title')
  const sortDesc = ref(false)
  const pageSize = ref(20)

  const ucid = computed(() => connectionStore.userConnectionId)
  const pageCount = computed(() => Math.max(1, Math.ceil(totalItems.value / pageSize.value)))
  const hasNoResults = computed(() => !isLoading.value && items.value.length === 0)

  // --- Debounce helper ---
  let searchTimeout: ReturnType<typeof setTimeout> | null = null

  function debouncedSearch() {
    if (searchTimeout) clearTimeout(searchTimeout)
    searchTimeout = setTimeout(() => {
      currentPage.value = 0
      loadItems()
    }, 300)
  }

  // Watch search query changes → debounced reload
  watch(searchQuery, () => {
    debouncedSearch()
  })

  // Watch sort/page-size changes → immediate reload
  watch([sortField, sortDesc, pageSize], () => {
    currentPage.value = 0
    loadItems()
  })

  // --- Actions ---

  async function loadLibraries() {
    if (!ucid.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getLibraries(ucid.value)
      libraries.value = data

      const defaultId = connectionStore.status?.defaultLibraryId
      if (defaultId && data.some((l: AbsLibrary) => l.id === defaultId)) {
        selectedLibraryId.value = defaultId
      } else if (data.length > 0) {
        selectedLibraryId.value = data[0].id
      }
    } finally {
      isLoading.value = false
    }
  }

  async function loadItems(page = 0, limit = pageSize.value) {
    if (!ucid.value || !selectedLibraryId.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getLibraryItems(
        ucid.value,
        selectedLibraryId.value,
        page,
        limit,
        false,
        searchQuery.value.trim() || undefined,
        sortField.value,
        sortDesc.value,
      )
      items.value = data.results
      totalItems.value = data.total
      currentPage.value = page
    } finally {
      isLoading.value = false
    }
  }

  async function loadSeries(page = 0, limit = 20) {
    if (!ucid.value || !selectedLibraryId.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getSeries(
        ucid.value, selectedLibraryId.value, page, limit,
      )
      series.value = data.results
      totalSeries.value = data.total
    } finally {
      isLoading.value = false
    }
  }

  async function loadBookDetail(itemId: string) {
    if (!ucid.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getBookDetail(ucid.value, itemId)
      currentBookDetail.value = data
    } finally {
      isLoading.value = false
    }
  }

  function getCoverUrl(itemId: string): string {
    if (!ucid.value) return ''
    return libraryApi.getCoverUrl(ucid.value, itemId)
  }

  function selectLibrary(libraryId: string) {
    selectedLibraryId.value = libraryId
    items.value = []
    series.value = []
    currentPage.value = 0
    searchQuery.value = ''
  }

  function clearSearch() {
    searchQuery.value = ''
  }

  return {
    // State
    libraries,
    selectedLibraryId,
    items,
    totalItems,
    currentPage,
    series,
    totalSeries,
    currentBookDetail,
    isLoading,
    viewMode,
    searchQuery,
    sortField,
    sortDesc,
    pageSize,
    // Computed
    pageCount,
    hasNoResults,
    // Actions
    loadLibraries,
    loadItems,
    loadSeries,
    loadBookDetail,
    getCoverUrl,
    selectLibrary,
    clearSearch,
  }
})
