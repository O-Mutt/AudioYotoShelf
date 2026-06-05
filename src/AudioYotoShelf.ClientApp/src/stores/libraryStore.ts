import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { libraryApi } from '@/services/api'
import { useConnectionStore } from './connectionStore'
import type { AbsLibrary, AbsLibraryItem, AbsSeriesItem, BookDetailResponse } from '@/types'

export type SortField =
  | 'media.metadata.title'
  | 'media.metadata.authorName'
  | 'media.duration'
  | 'addedAt'

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
  const seriesPage = ref(0)
  const seriesPageSize = ref(100)
  const currentBookDetail = ref<BookDetailResponse | null>(null)
  const isLoading = ref(false)

  // Persist the books/series toggle so it survives a refresh.
  const VIEW_MODE_KEY = 'ays:library:viewMode'
  const viewMode = ref<'books' | 'series'>(readViewMode())
  watch(viewMode, (mode) => {
    try {
      localStorage.setItem(VIEW_MODE_KEY, mode)
    } catch {
      /* storage unavailable */
    }
  })

  // Search, sort & pagination state
  const searchQuery = ref('')
  const sortField = ref<SortField>('media.metadata.title')
  const sortDesc = ref(false)
  const pageSize = ref(100)

  const ucid = computed(() => connectionStore.userConnectionId)
  const pageCount = computed(() => Math.max(1, Math.ceil(totalItems.value / pageSize.value)))
  const pageCountSeries = computed(() =>
    Math.max(1, Math.ceil(totalSeries.value / seriesPageSize.value)),
  )
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

  // Series page-size change → reload first series page
  watch(seriesPageSize, () => {
    if (viewMode.value === 'series') loadSeries(0)
  })

  // --- Actions ---

  async function loadLibraries() {
    if (!ucid.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getLibraries()
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

  async function loadSeries(page = 0, limit = seriesPageSize.value) {
    if (!ucid.value || !selectedLibraryId.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getSeries(selectedLibraryId.value, page, limit)
      series.value = data.results
      totalSeries.value = data.total
      seriesPage.value = page
    } finally {
      isLoading.value = false
    }
  }

  async function loadBookDetail(itemId: string) {
    if (!ucid.value) return
    isLoading.value = true
    try {
      const { data } = await libraryApi.getBookDetail(itemId)
      currentBookDetail.value = data
    } finally {
      isLoading.value = false
    }
  }

  function getCoverUrl(itemId: string): string {
    if (!ucid.value) return ''
    return libraryApi.getCoverUrl(itemId)
  }

  function selectLibrary(libraryId: string) {
    selectedLibraryId.value = libraryId
    items.value = []
    series.value = []
    currentPage.value = 0
    seriesPage.value = 0
    searchQuery.value = ''
  }

  function clearSearch() {
    searchQuery.value = ''
  }

  function readViewMode(): 'books' | 'series' {
    try {
      return localStorage.getItem('ays:library:viewMode') === 'series' ? 'series' : 'books'
    } catch {
      return 'books'
    }
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
    seriesPage,
    seriesPageSize,
    currentBookDetail,
    isLoading,
    viewMode,
    searchQuery,
    sortField,
    sortDesc,
    pageSize,
    // Computed
    pageCount,
    pageCountSeries,
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
