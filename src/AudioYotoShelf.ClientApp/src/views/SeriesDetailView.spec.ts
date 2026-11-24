import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { libraryApi, transferApi } from '@/services/api'
import { useConnectionStore } from '@/stores/connectionStore'
import type { AbsSeriesItem, ConnectionStatus } from '@/types'
import SeriesDetailView from '@/views/SeriesDetailView.vue'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

vi.mock('@/services/api', () => ({
  libraryApi: { getSeriesDetail: vi.fn() },
  transferApi: { transferSeries: vi.fn() },
  authApi: { getConnectionStatus: vi.fn(), logout: vi.fn() },
}))

const QUEUED =
  'Series transfer queued. A book larger than one Yoto card is not sent; it shows on the ' +
  'Transfers page as failed, with the reason.'

describe('SeriesDetailView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    const connection = useConnectionStore()
    connection.userConnectionId = 'c1'
    connection.status = { id: 'c1', yotoConnected: true, absConnected: true } as ConnectionStatus
  })

  it('shows what the server says after "One card per book" is queued', async () => {
    vi.mocked(libraryApi.getSeriesDetail).mockResolvedValue({
      data: {
        id: 's1',
        name: 'Harry Potter',
        description: null,
        books: [],
        totalDuration: 0,
      } as AbsSeriesItem,
    } as never)
    vi.mocked(transferApi.transferSeries).mockResolvedValue({
      data: { jobId: 'job-1', message: QUEUED },
    } as never)
    const wrapper = mount(SeriesDetailView, {
      props: { seriesId: 's1', libraryId: 'lib-1' },
      global: {
        stubs: { CardPlanPanel: { template: '<div><slot name="secondary-actions" /></div>' } },
      },
    })
    await flushPromises()

    await wrapper.find('button.btn-secondary').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain(QUEUED)
  })
})
