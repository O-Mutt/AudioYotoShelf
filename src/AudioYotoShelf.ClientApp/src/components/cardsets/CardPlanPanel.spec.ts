import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { cardSetApi } from '@/services/api'
import { useCardSetStore } from '@/stores/cardSetStore'
import CardPlanPanel from '@/components/cardsets/CardPlanPanel.vue'
import type { CardSetCard, CardSetResponse } from '@/types'

vi.mock('@/services/api', () => ({
  cardSetApi: {
    plan: vi.fn(),
    create: vi.fn(),
    list: vi.fn(),
    get: vi.fn(),
    updateBreaks: vi.fn(),
    update: vi.fn(),
    renameCard: vi.fn(),
    remove: vi.fn(),
    transfer: vi.fn(),
  },
  authApi: { getConnectionStatus: vi.fn() },
}))

const api = vi.mocked(cardSetApi)
const ok = <T>(data: T) => Promise.resolve({ data }) as never

function card(
  index: number,
  first: number,
  last: number,
  extra: Partial<CardSetCard> = {},
): CardSetCard {
  return {
    index,
    firstBookIndex: first,
    lastBookIndex: last,
    firstChapterIndex: null,
    lastChapterIndex: null,
    partNumber: null,
    partCount: null,
    title: `Dragons · Books ${first + 1}–${last + 1}`,
    capacity: {
      tracks: 2,
      maxTracks: 100,
      durationSeconds: 7200,
      maxDurationSeconds: null,
      bytes: 460_500_000,
      maxBytes: 524_288_000,
      withinLimits: true,
      exceedsTracks: false,
      exceedsDuration: false,
      exceedsBytes: false,
      firstOverflowItemId: null,
      messages: [],
    },
    playlistId: `p${index}`,
    status: 'Draft',
    locked: false,
    yotoCardId: null,
    errorMessage: null,
    ...extra,
  }
}

const books = [0, 1, 2, 3].map((i) => ({
  absLibraryItemId: `b${i}`,
  title: `Book ${i + 1}`,
  sequence: i + 1,
  durationSeconds: 3600,
  estimatedBytes: 45_000_000,
  split: false,
  chapters: null,
}))

function saved(cards: CardSetCard[]): CardSetResponse {
  return {
    id: 'set-1',
    name: 'Dragons',
    absSeriesId: 'ser-1',
    absLibraryId: 'lib-1',
    packingMode: 'Balance',
    excludedItemIds: [],
    status: 'Draft',
    books,
    cards,
    withinLimits: true,
    messages: [],
  }
}

function mountPanel() {
  return mount(CardPlanPanel, {
    props: {
      seriesId: 'ser-1',
      libraryId: 'lib-1',
      seriesBooks: books.map((b) => ({
        id: b.absLibraryItemId,
        title: b.title,
        sequence: String(b.sequence),
      })),
    },
  })
}

describe('CardPlanPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('shows each card with its size against the limit', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(ok(saved([card(0, 0, 1), card(1, 2, 3)])))

    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-testid="card-0"]').text()).toContain('≈ 439 MB of 500 MB')
    expect(wrapper.find('[data-testid="card-1"]').text()).toContain('Book 3')
    useCardSetStore().stopPolling()
  })

  it('toggling the mode re-plans', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValue(
      ok({
        seriesName: 'Dragons',
        books,
        excludedBooks: [],
        cards: [card(0, 0, 3)],
        withinLimits: true,
        unsplittableBookIds: [],
        messages: [],
      }),
    )

    const wrapper = mountPanel()
    await flushPromises()
    await wrapper.find('[data-testid="mode-Fill"]').trigger('click')
    await flushPromises()

    expect(api.plan).toHaveBeenLastCalledWith(expect.objectContaining({ mode: 'Fill' }))
  })

  it('disables the break next to a card already on Yoto', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(
      ok(saved([card(0, 0, 1, { status: 'Transferred', locked: true }), card(1, 2, 3)])),
    )

    const wrapper = mountPanel()
    await flushPromises()

    expect(wrapper.find('[data-testid="card-0-status"]').text()).toBe('On Yoto')
    expect(wrapper.find('[data-testid="break-1-earlier"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="break-1-later"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="send-cards"]').text()).toBe('Send 1 card')
  })
})
