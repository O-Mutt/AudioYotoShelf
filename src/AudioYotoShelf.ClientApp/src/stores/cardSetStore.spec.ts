import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, AxiosHeaders } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cardSetApi } from '@/services/api'
import { layoutOf, useCardSetStore } from '@/stores/cardSetStore'
import type { CardSetBook, CardSetCard, CardSetPlanResponse, CardSetResponse } from '@/types'

const signalR = vi.hoisted(() => ({
  cardSetUpdate: null as never,
  joinCardSet: vi.fn(),
  leaveCardSet: vi.fn(),
}))

vi.mock('@/composables/useSignalR', async () => {
  const { ref } = await import('vue')
  signalR.cardSetUpdate = ref(null) as never
  return {
    useSignalR: () => ({
      cardSetUpdate: signalR.cardSetUpdate,
      joinCardSet: signalR.joinCardSet,
      leaveCardSet: signalR.leaveCardSet,
    }),
  }
})

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
    refresh: vi.fn(),
  },
}))

const api = vi.mocked(cardSetApi)
const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve, 0))
const ok = <T>(data: T) => Promise.resolve({ data }) as never

function book(i: number, chapters: number | null = null): CardSetBook {
  return {
    absLibraryItemId: `b${i}`,
    title: `Book ${i}`,
    sequence: i + 1,
    durationSeconds: 3600,
    estimatedBytes: 45_000_000,
    split: chapters != null,
    chapters:
      chapters == null
        ? null
        : Array.from({ length: chapters }, (_, c) => ({
            index: c,
            title: `Ch ${c + 1}`,
            durationSeconds: 600,
          })),
  }
}

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
    title: `Card ${index + 1}`,
    capacity: {
      tracks: last - first + 1,
      maxTracks: 100,
      durationSeconds: 3600,
      maxDurationSeconds: null,
      bytes: 45_000_000,
      maxBytes: 524_288_000,
      withinLimits: true,
      exceedsTracks: false,
      exceedsDuration: false,
      exceedsBytes: false,
      firstOverflowItemId: null,
      messages: [],
    },
    playlistId: null,
    status: null,
    locked: false,
    yotoCardId: null,
    errorMessage: null,
    ...extra,
  }
}

const books = [book(0), book(1), book(2), book(3)]

function plan(
  cards: CardSetCard[],
  withinLimits = true,
  messages: string[] = [],
): CardSetPlanResponse {
  return {
    seriesName: 'Dragons',
    books,
    excludedBooks: [],
    cards,
    withinLimits,
    unsplittableBookIds: [],
    messages,
  }
}

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
    newBooks: null,
  }
}

describe('layoutOf', () => {
  it('turns cards into breaks and split points', () => {
    const cards = [
      card(0, 0, 1),
      card(1, 2, 2, { firstChapterIndex: 0, lastChapterIndex: 4, partNumber: 1, partCount: 2 }),
      card(2, 2, 2, { firstChapterIndex: 5, lastChapterIndex: 9, partNumber: 2, partCount: 2 }),
      card(3, 3, 3),
    ]

    expect(layoutOf(cards, [book(0), book(1), book(2, 10), book(3)])).toEqual({
      breaks: [2, 3],
      splitPoints: { b2: [5] },
    })
  })
})

describe('cardSetStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })
  afterEach(() => useCardSetStore().stopPolling())

  it('previews a plan when the series has no set yet', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValue(ok(plan([card(0, 0, 1), card(1, 2, 3)])))
    const store = useCardSetStore()

    await store.load('ser-1', 'lib-1')

    expect(api.plan).toHaveBeenCalledWith(
      expect.objectContaining({ absSeriesId: 'ser-1', mode: 'Balance', breaks: null }),
    )
    expect(store.cards).toHaveLength(2)
    expect(store.isSaved).toBe(false)
  })

  it('loads the saved set when there is one', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Draft' })])))
    const store = useCardSetStore()

    await store.load('ser-1', 'lib-1')

    expect(api.plan).not.toHaveBeenCalled()
    expect(store.isSaved).toBe(true)
  })

  it('re-plans when the mode changes', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValue(ok(plan([card(0, 0, 3)])))
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')

    await store.setMode('Fill')

    expect(api.plan).toHaveBeenLastCalledWith(expect.objectContaining({ mode: 'Fill' }))
    expect(store.mode).toBe('Fill')
  })

  it('moves a break by re-planning with the new breaks', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValueOnce(ok(plan([card(0, 0, 1), card(1, 2, 3)])))
    api.plan.mockReturnValueOnce(ok(plan([card(0, 0, 0), card(1, 1, 3)])))
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')

    await store.moveBreak(1, -1)

    expect(api.plan).toHaveBeenLastCalledWith(expect.objectContaining({ breaks: [1] }))
    expect(store.cards[1]!.firstBookIndex).toBe(1)
    expect(store.error).toBeNull()
  })

  it('snaps an invalid break back and says which card overflowed', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValueOnce(ok(plan([card(0, 0, 1), card(1, 2, 3)])))
    api.plan.mockReturnValueOnce(
      ok(plan([card(0, 0, 2), card(1, 3, 3)], false, ['Card 1 does not fit: too big.'])),
    )
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')

    await store.moveBreak(1, 1)

    expect(store.cards[0]!.lastBookIndex).toBe(1)
    expect(store.error).toContain('Card 1 does not fit')
  })

  it('snaps a refused break on a saved set back', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(
      ok(saved([card(0, 0, 1, { playlistId: 'p1' }), card(1, 2, 3, { playlistId: 'p2' })])),
    )
    api.updateBreaks.mockRejectedValue(
      new AxiosError('422', '422', undefined, undefined, {
        status: 422,
        statusText: '',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: {
          message: 'A card in this plan does not fit.',
          plan: { messages: ['Card 2 does not fit.'] },
        },
      }),
    )
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')

    await store.moveBreak(1, 1)

    expect(api.updateBreaks).toHaveBeenCalledWith('set-1', [3], {})
    expect(store.cards[1]!.firstBookIndex).toBe(2)
    expect(store.error).toBe('Card 2 does not fit.')
  })

  it('does not move a break next to a card already on Yoto', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(
      ok(
        saved([
          card(0, 0, 1, { playlistId: 'p1', status: 'Transferred', locked: true }),
          card(1, 2, 3, { playlistId: 'p2' }),
        ]),
      ),
    )
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')

    expect(store.canMoveBreak(1, -1)).toBe(false)
    expect(store.canMoveBreak(1, 1)).toBe(false)
    await store.moveBreak(1, 1)
    expect(api.updateBreaks).not.toHaveBeenCalled()
  })

  it('makes the cards and sends them all, then shows them queued', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValue(ok(plan([card(0, 0, 3)])))
    api.create.mockReturnValue(ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Draft' })])))
    api.transfer.mockReturnValue(ok({ cardSetId: 'set-1', playlistIds: ['p1'] }))
    api.get.mockReturnValue(
      ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Queued', locked: true })])),
    )
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')

    await store.makeCards()

    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({ absSeriesId: 'ser-1', mode: 'Balance' }),
    )
    expect(api.transfer).toHaveBeenCalledWith('set-1', undefined)
    expect(store.isSaved).toBe(true)
    expect(store.cards[0]!.status).toBe('Queued')
    expect(store.isSending).toBe(true)
  })

  describe('while cards are sending', () => {
    beforeEach(() => vi.useFakeTimers())
    afterEach(() => vi.useRealTimers())

    it('after a reload, keeps reading the set until the card being sent lands', async () => {
      api.list.mockReturnValue(ok([{ id: 'set-1' }]))
      api.get.mockReturnValueOnce(
        ok(
          saved([
            card(0, 0, 1, { playlistId: 'p1', status: 'Transferred', locked: true }),
            card(1, 2, 3, { playlistId: 'p2', status: 'Transferring', locked: true }),
          ]),
        ),
      )
      api.get.mockReturnValue(
        ok(
          saved([
            card(0, 0, 1, { playlistId: 'p1', status: 'Transferred', locked: true }),
            card(1, 2, 3, { playlistId: 'p2', status: 'Transferred', locked: true }),
          ]),
        ),
      )
      const store = useCardSetStore()
      await store.load('ser-1', 'lib-1')
      expect(store.isSending).toBe(true)

      await vi.advanceTimersByTimeAsync(5000)

      expect(store.cards[1]!.status).toBe('Transferred')
      expect(store.isSending).toBe(false)
      await vi.advanceTimersByTimeAsync(15000)
      expect(api.get).toHaveBeenCalledTimes(2)
    })

    it('does not poll a draft card nobody sent', async () => {
      api.list.mockReturnValue(ok([{ id: 'set-1' }]))
      api.get.mockReturnValue(ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Draft' })])))
      const store = useCardSetStore()
      await store.load('ser-1', 'lib-1')

      await vi.advanceTimersByTimeAsync(15000)

      expect(api.get).toHaveBeenCalledTimes(1)
    })

    it('a retried card keeps being read until it is sent', async () => {
      api.list.mockReturnValue(ok([{ id: 'set-1' }]))
      api.get.mockReturnValueOnce(
        ok(
          saved([
            card(0, 0, 3, { playlistId: 'p1', status: 'Failed', errorMessage: 'Yoto said no' }),
          ]),
        ),
      )
      api.transfer.mockReturnValue(ok({ cardSetId: 'set-1', playlistIds: ['p1'] }))
      api.get.mockReturnValueOnce(
        ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Queued', locked: true })])),
      )
      api.get.mockReturnValueOnce(
        ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Queued', locked: true })])),
      )
      api.get.mockReturnValue(
        ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Transferred', locked: true })])),
      )
      const store = useCardSetStore()
      await store.load('ser-1', 'lib-1')

      await store.send(['p1'])
      expect(store.cards[0]!.status).toBe('Queued')
      await vi.advanceTimersByTimeAsync(5000)
      expect(store.isSending).toBe(true)
      await vi.advanceTimersByTimeAsync(5000)

      expect(store.cards[0]!.status).toBe('Transferred')
      expect(store.isSending).toBe(false)
    })
  })

  it('reports books the planner could not split', async () => {
    api.list.mockReturnValue(ok([]))
    api.plan.mockReturnValue(ok({ ...plan([card(0, 0, 2)]), unsplittableBookIds: ['b3'] }))
    const store = useCardSetStore()

    await store.load('ser-1', 'lib-1')

    expect(store.unsplittable).toEqual(['b3'])
  })

  it('keeps the new books a read of the set found, and places them on request', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(
      ok({
        ...saved([card(0, 0, 3, { playlistId: 'p1', status: 'Draft' })]),
        newBooks: [{ absLibraryItemId: 'b4', title: 'Book 4', sequence: 5 }],
      }),
    )
    api.refresh.mockReturnValue(
      ok({ ...saved([card(0, 0, 3, { playlistId: 'p1', status: 'Draft' })]), newBooks: [] }),
    )
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')
    expect(store.newBooks.map((b) => b.absLibraryItemId)).toEqual(['b4'])
    expect(signalR.joinCardSet).toHaveBeenCalledWith('set-1')

    await store.addNewBooks()

    expect(api.refresh).toHaveBeenCalledWith('set-1')
    expect(store.newBooks).toEqual([])
  })

  it('re-reads the set when a card of it changes, but not for another set', async () => {
    api.list.mockReturnValue(ok([{ id: 'set-1' }]))
    api.get.mockReturnValue(
      ok(saved([card(0, 0, 3, { playlistId: 'p1', status: 'Queued', locked: true })])),
    )
    const store = useCardSetStore()
    await store.load('ser-1', 'lib-1')
    api.get.mockClear()
    const update = (cardSetId: string) =>
      ({
        cardSetId,
        playlistId: 'p1',
        cardIndex: 0,
        status: 'Transferring',
        errorMessage: null,
      }) as never

    ;(signalR.cardSetUpdate as unknown as { value: unknown }).value = update('other-set')
    await flushMicrotasks()
    expect(api.get).not.toHaveBeenCalled()

    ;(signalR.cardSetUpdate as unknown as { value: unknown }).value = update('set-1')
    await flushMicrotasks()
    expect(api.get).toHaveBeenCalledWith('set-1', false)
  })
})
