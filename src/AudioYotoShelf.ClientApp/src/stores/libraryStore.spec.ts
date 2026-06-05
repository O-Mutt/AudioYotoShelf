import { setActivePinia, createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { libraryApi } from '@/services/api'
import { useConnectionStore } from '@/stores/connectionStore'
import { useLibraryStore } from '@/stores/libraryStore'

vi.mock('@/services/api', () => ({
  authApi: { getConnectionStatus: vi.fn(), logout: vi.fn(() => Promise.resolve({ data: {} })) },
  libraryApi: {
    getLibraries: vi.fn(),
    getLibraryItems: vi.fn(),
    getSeries: vi.fn(),
    getBookDetail: vi.fn(),
    getCoverUrl: vi.fn(),
  },
}))

describe('libraryStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.clearAllMocks()
  })

  it('does not hit the API before a connection exists', async () => {
    const lib = useLibraryStore()

    await lib.loadLibraries()

    expect(libraryApi.getLibraries).not.toHaveBeenCalled()
  })

  it('loads libraries and selects the first when no default is set', async () => {
    useConnectionStore().setUserConnectionId('conn-1')
    vi.mocked(libraryApi.getLibraries).mockResolvedValue({
      data: [
        { id: 'lib-1', name: 'A', mediaType: 'book' },
        { id: 'lib-2', name: 'B', mediaType: 'book' },
      ],
    } as never)
    const lib = useLibraryStore()

    await lib.loadLibraries()

    expect(libraryApi.getLibraries).toHaveBeenCalledOnce()
    expect(lib.libraries).toHaveLength(2)
    expect(lib.selectedLibraryId).toBe('lib-1')
  })

  it('getCoverUrl returns empty string until connected, then delegates to the api', () => {
    const lib = useLibraryStore()
    expect(lib.getCoverUrl('item-1')).toBe('')

    useConnectionStore().setUserConnectionId('conn-1')
    vi.mocked(libraryApi.getCoverUrl).mockReturnValue('/api/libraries/items/item-1/cover')
    expect(lib.getCoverUrl('item-1')).toBe('/api/libraries/items/item-1/cover')
  })

  it('selectLibrary switches library and clears the search query', () => {
    const lib = useLibraryStore()

    lib.selectLibrary('lib-2')

    expect(lib.selectedLibraryId).toBe('lib-2')
    expect(lib.searchQuery).toBe('')
  })
})
