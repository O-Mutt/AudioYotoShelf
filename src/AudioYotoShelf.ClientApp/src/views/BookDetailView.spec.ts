import { flushPromises, mount } from '@vue/test-utils'
import { AxiosError, AxiosHeaders } from 'axios'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { libraryApi, transferApi } from '@/services/api'
import { useConnectionStore } from '@/stores/connectionStore'
import type { BookDetailResponse, ConnectionStatus } from '@/types'
import BookDetailView from '@/views/BookDetailView.vue'

vi.mock('@/composables/useSignalR', async () => {
  const { ref } = await import('vue')
  return {
    useSignalR: () => ({
      progressUpdates: ref(new Map()),
      connect: vi.fn(() => Promise.resolve()),
      joinTransfer: vi.fn(() => Promise.resolve()),
    }),
  }
})

vi.mock('@/composables/useConfirm', () => ({
  useConfirm: () => ({ confirm: () => Promise.resolve(true) }),
}))

vi.mock('@/services/api', () => ({
  libraryApi: { getBookDetail: vi.fn(), getCoverUrl: vi.fn(() => '') },
  transferApi: { transferBook: vi.fn() },
  authApi: { getConnectionStatus: vi.fn(), logout: vi.fn() },
}))

const REFUSAL =
  '"Goblet of Fire" is larger than one Yoto card, so it was not sent. Estimated size 868MB exceeds ' +
  'the limit of 500MB. It is in the series "Harry Potter": the card plan on that series page can ' +
  'split it across cards.'

function conflict(message: string) {
  return new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST', undefined, null, {
    status: 409,
    statusText: 'Conflict',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: { message },
  })
}

async function mountBook() {
  vi.mocked(libraryApi.getBookDetail).mockResolvedValue({
    data: {
      item: {
        id: 'item-1',
        media: { metadata: { title: 'Goblet of Fire' }, duration: 72000, chapters: [] },
      },
      ageSuggestion: null,
      existingTransfer: null,
    } as unknown as BookDetailResponse,
  } as never)
  const wrapper = mount(BookDetailView, { props: { itemId: 'item-1' } })
  await flushPromises()
  return wrapper
}

describe('BookDetailView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    const connection = useConnectionStore()
    connection.userConnectionId = 'c1'
    connection.status = { id: 'c1', yotoConnected: true, absConnected: true } as ConnectionStatus
  })

  it('shows why the server refused a book that is larger than one card, and keeps it on screen', async () => {
    vi.mocked(transferApi.transferBook).mockRejectedValue(conflict(REFUSAL))
    const wrapper = await mountBook()

    await wrapper.find('button.btn-primary').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-test="transfer-refusal"]').text()).toBe(REFUSAL)
  })

  it('clears the refusal when the next transfer is queued', async () => {
    vi.mocked(transferApi.transferBook).mockRejectedValueOnce(conflict(REFUSAL))
    vi.mocked(transferApi.transferBook).mockResolvedValueOnce({ data: {} } as never)
    const wrapper = await mountBook()

    await wrapper.find('button.btn-primary').trigger('click')
    await flushPromises()
    await wrapper.find('button.btn-primary').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-test="transfer-refusal"]').exists()).toBe(false)
  })
})
