import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cardSetApi, transferApi } from '@/services/api'
import { useConnectionStore } from '@/stores/connectionStore'
import type {
  CardSetProgressUpdate,
  CardSetSend,
  TransferProgressUpdate,
  TransferResponse,
  TransferStatus,
} from '@/types'
import TransfersView from '@/views/TransfersView.vue'

// The SignalR refs are shared, so a page left mounted by one test would react to the next test's pushes.
enableAutoUnmount(afterEach)

const signalR = vi.hoisted(() => ({
  progressUpdates: null as never,
  trackStates: null as never,
  listChangedAt: null as never,
  cardSetUpdate: null as never,
  joinCardSet: vi.fn(),
  leaveCardSet: vi.fn(),
}))

vi.mock('@/composables/useSignalR', async () => {
  const { ref } = await import('vue')
  signalR.progressUpdates = ref(new Map()) as never
  signalR.trackStates = ref({}) as never
  signalR.listChangedAt = ref(0) as never
  signalR.cardSetUpdate = ref(null) as never
  return {
    useSignalR: () => ({
      progressUpdates: signalR.progressUpdates,
      trackStates: signalR.trackStates,
      listChangedAt: signalR.listChangedAt,
      cardSetUpdate: signalR.cardSetUpdate,
      joinCardSet: signalR.joinCardSet,
      leaveCardSet: signalR.leaveCardSet,
      connect: vi.fn(() => Promise.resolve()),
      joinTransfer: vi.fn(() => Promise.resolve()),
    }),
  }
})

vi.mock('@/services/api', () => ({
  transferApi: { getTransfers: vi.fn(), clearCompleted: vi.fn() },
  cardSetApi: { sends: vi.fn() },
  authApi: { getConnectionStatus: vi.fn(), logout: vi.fn() },
}))

const TRANSFER_ID = 't-1'

function transfer(status: TransferStatus, uploadedTracks = 1, trackCount = 3): TransferResponse {
  return {
    id: TRANSFER_ID,
    absLibraryItemId: 'item-1',
    bookTitle: "Harry Potter and the Philosopher's Stone",
    bookAuthor: 'J.K. Rowling',
    seriesName: null,
    seriesSequence: null,
    status,
    progressPercent: 21,
    errorMessage: null,
    ageRange: {
      suggestedMin: 8,
      suggestedMax: 15,
      suggestionReason: '',
      effectiveMin: 8,
      effectiveMax: 15,
    },
    yotoCardId: null,
    createdAt: '2026-09-20T19:11:22Z',
    completedAt: null,
    tracks: Array.from({ length: trackCount }, (_, i) => ({
      id: `track-${i}`,
      chapterTitle: `Chapter ${i + 1}`,
      chapterIndex: i,
      duration: 600,
      isUploaded: i < uploadedTracks,
      iconUrl: null,
    })),
  } as TransferResponse
}

async function mountWith(t: TransferResponse | null, sets: CardSetSend[] = [], olderCount = 0) {
  vi.mocked(transferApi.getTransfers).mockResolvedValue({
    data: { results: t ? [t] : [], total: t ? 1 : 0 },
  } as never)
  vi.mocked(cardSetApi.sends).mockResolvedValue({ data: { sets, olderCount } } as never)
  const wrapper = mount(TransfersView, { global: { stubs: { 'router-link': true } } })
  await flushPromises()
  return wrapper
}

const SET_ID = 'set-1'

function cardSet(status: CardSetSend['status'], extra: Partial<CardSetSend> = {}): CardSetSend {
  return {
    id: SET_ID,
    name: 'PAW Patrol',
    absSeriesId: 'ser-1',
    absLibraryId: 'lib-1',
    status,
    cardCount: 2,
    sentCount: 0,
    lastActivityAt: new Date().toISOString(),
    cards: [
      {
        playlistId: 'card-1',
        index: 0,
        title: 'PAW Patrol · Books 1–5',
        status: 'Transferring',
        yotoCardId: null,
        errorMessage: null,
        transferredAt: null,
        bookTitles: ['Pups Save the Day', 'Ready for Action'],
      },
      {
        playlistId: 'card-2',
        index: 1,
        title: 'PAW Patrol · Books 6–9',
        status: 'Queued',
        yotoCardId: null,
        errorMessage: null,
        transferredAt: null,
        bookTitles: ['Pups Save Christmas'],
      },
    ],
    ...extra,
  }
}

function pushLiveUpdate(update: Partial<TransferProgressUpdate>) {
  const updates = signalR.progressUpdates as unknown as ReturnType<
    typeof ref<Map<string, TransferProgressUpdate>>
  >
  updates.value = new Map([
    [
      TRANSFER_ID,
      {
        transferId: TRANSFER_ID,
        status: 'UploadingToYoto',
        progressPercent: 24,
        currentStep: null,
        errorMessage: null,
        ...update,
      },
    ],
  ])
}

describe('TransfersView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useConnectionStore().userConnectionId = 'c1'
    const updates = signalR.progressUpdates as unknown as ReturnType<
      typeof ref<Map<string, unknown>>
    >
    updates.value = new Map()
  })

  it('does not call an upload that is mostly Yoto transcoding just "Uploading"', async () => {
    const wrapper = await mountWith(transfer('UploadingToYoto'))

    expect(wrapper.text()).toContain('Uploading & transcoding')
  })

  it('shows what the server says it is doing, live, under the progress bar', async () => {
    const wrapper = await mountWith(transfer('UploadingToYoto'))

    pushLiveUpdate({ currentStep: 'Transcoding track 2/17 on Yoto… 55%' })
    await flushPromises()

    expect(wrapper.find('[data-test="transfer-step"]').text()).toBe(
      'Transcoding track 2/17 on Yoto… 55%',
    )
  })

  it('keeps showing the latest step as new ones arrive', async () => {
    const wrapper = await mountWith(transfer('UploadingToYoto'))

    pushLiveUpdate({ currentStep: 'Transcoding track 2/17 on Yoto… 55%' })
    await flushPromises()
    pushLiveUpdate({ currentStep: 'Uploading track 3/17…' })
    await flushPromises()

    expect(wrapper.find('[data-test="transfer-step"]').text()).toBe('Uploading track 3/17…')
  })

  it('shows no step line until the server has said something', async () => {
    const wrapper = await mountWith(transfer('UploadingToYoto'))

    expect(wrapper.find('[data-test="transfer-step"]').exists()).toBe(false)
  })

  it('says how many tracks are on Yoto, and opens to show each one', async () => {
    const wrapper = await mountWith(transfer('UploadingToYoto', 1, 3))

    expect(wrapper.find('[data-test="tracks-toggle"]').text()).toContain('1 of 3 tracks on Yoto')
    await wrapper.find('[data-test="tracks-toggle"]').trigger('click')
    expect(wrapper.findAll('[data-test="track-row"]')).toHaveLength(3)
  })

  it('shows what Yoto is doing with a track as soon as the server says it, without a reload', async () => {
    const wrapper = await mountWith(transfer('UploadingToYoto', 1, 3))
    await wrapper.find('[data-test="tracks-toggle"]').trigger('click')

    const states = signalR.trackStates as unknown as ReturnType<typeof ref<Record<string, unknown>>>
    states.value = { [TRANSFER_ID]: { 'track-1': { phase: 'Transcoding', percent: 55 } } }
    await flushPromises()

    const second = wrapper.findAll('[data-test="track-row"]')[1]
    expect(second.text()).toContain('Yoto is processing')
    expect(second.text()).toContain('55%')
  })

  it("still shows a failed transfer's tracks, so you can see where it stopped", async () => {
    const wrapper = await mountWith(transfer('Failed', 1, 3))
    await wrapper.find('[data-test="tracks-toggle"]').trigger('click')

    expect(wrapper.findAll('[data-test="track-row"]')[1].text()).toContain('Stopped here')
  })

  it('shows no step line for a transfer that is not running', async () => {
    const wrapper = await mountWith(transfer('Failed'))

    expect(wrapper.find('[data-test="transfer-step"]').exists()).toBe(false)
  })
})

describe('TransfersView card sets', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useConnectionStore().userConnectionId = 'c1'
    ;(signalR.cardSetUpdate as unknown as { value: unknown }).value = null
  })

  function pushCardUpdate(update: Partial<CardSetProgressUpdate>) {
    ;(signalR.cardSetUpdate as unknown as { value: CardSetProgressUpdate }).value = {
      cardSetId: SET_ID,
      playlistId: 'card-1',
      cardIndex: 0,
      status: 'Transferred',
      errorMessage: null,
      ...update,
    }
  }

  it('hides the section when no set was ever sent', async () => {
    const wrapper = await mountWith(transfer('Completed'))

    expect(wrapper.find('[data-test="card-sets"]').exists()).toBe(false)
  })

  it('lists a sending set next to the book transfers, with how many cards are sent', async () => {
    const wrapper = await mountWith(transfer('Completed'), [cardSet('Sending')])

    const row = wrapper.find('[data-test="card-set"]')
    expect(row.text()).toContain('PAW Patrol')
    expect(row.find('[data-test="card-set-count"]').text()).toBe('0 of 2 cards sent')
    expect(row.find('[data-test="card-set-status"]').text()).toBe('Sending')
    expect(wrapper.text()).toContain("Harry Potter and the Philosopher's Stone")
  })

  it('shows a set even when there are no book transfers', async () => {
    const wrapper = await mountWith(null, [cardSet('Complete')])

    expect(wrapper.find('[data-test="card-set"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('No transfers yet')
  })

  it('opens a set to show each card with its books, its Yoto card and its error', async () => {
    const set = cardSet('NeedsAttention')
    set.cards[0] = { ...set.cards[0], status: 'Transferred', yotoCardId: 'hLH5V' }
    set.cards[1] = { ...set.cards[1], status: 'Failed', errorMessage: 'Yoto said no' }
    const wrapper = await mountWith(null, [set])

    expect(wrapper.find('[data-test="card-set-cards"]').exists()).toBe(false)
    await wrapper.find('[data-test="card-set"] button').trigger('click')

    const cards = wrapper.findAll('[data-test="card-set-card"]')
    expect(cards).toHaveLength(2)
    expect(cards[0].text()).toContain('hLH5V')
    expect(cards[0].text()).toContain('Pups Save the Day · Ready for Action')
    expect(cards[0].find('[data-test="card-status"]').text()).toBe('On Yoto')
    expect(cards[1].find('[data-test="card-error"]').text()).toBe('Yoto said no')
  })

  it('listens for progress on a sending set, and only on that one', async () => {
    await mountWith(null, [cardSet('Sending'), cardSet('Complete', { id: 'set-2' })])

    expect(signalR.joinCardSet).toHaveBeenCalledTimes(1)
    expect(signalR.joinCardSet).toHaveBeenCalledWith(SET_ID)
  })

  it('updates a card in place when its progress is pushed', async () => {
    const wrapper = await mountWith(null, [cardSet('Sending')])
    await wrapper.find('[data-test="card-set"] button').trigger('click')

    pushCardUpdate({ playlistId: 'card-1', status: 'Transferred' })
    await flushPromises()

    const first = wrapper.findAll('[data-test="card-set-card"]')[0]
    expect(first.find('[data-test="card-status"]').text()).toBe('On Yoto')
    expect(wrapper.find('[data-test="card-set-count"]').text()).toBe('1 of 2 cards sent')
  })

  it('shows a pushed failure with its message', async () => {
    const wrapper = await mountWith(null, [cardSet('Sending')])
    await wrapper.find('[data-test="card-set"] button').trigger('click')

    pushCardUpdate({
      playlistId: 'card-2',
      status: 'Failed',
      errorMessage: 'No valid Yoto connection',
    })
    await flushPromises()

    expect(wrapper.find('[data-test="card-error"]').text()).toBe('No valid Yoto connection')
  })

  it('ignores progress for a set it does not show', async () => {
    const wrapper = await mountWith(null, [cardSet('Sending')])

    pushCardUpdate({ cardSetId: 'another-set', status: 'Transferred' })
    await flushPromises()

    expect(wrapper.find('[data-test="card-set-count"]').text()).toBe('0 of 2 cards sent')
  })

  it('re-reads the sets when the list changes, and stops listening to a set that finished', async () => {
    await mountWith(null, [cardSet('Sending')])
    vi.mocked(cardSetApi.sends).mockResolvedValue({
      data: { sets: [cardSet('Complete')], olderCount: 0 },
    } as never)

    ;(signalR.listChangedAt as unknown as { value: number }).value = Date.now()
    await flushPromises()

    expect(cardSetApi.sends).toHaveBeenCalledTimes(2)
    expect(signalR.leaveCardSet).toHaveBeenCalledWith(SET_ID)
  })

  it('leaves the set groups it joined when the page closes', async () => {
    const wrapper = await mountWith(null, [cardSet('Sending')])

    wrapper.unmount()
    await flushPromises()

    expect(signalR.leaveCardSet).toHaveBeenCalledWith(SET_ID)
  })

  it('maps the status filter onto set status', async () => {
    const wrapper = await mountWith(null, [cardSet('Sending')])

    await wrapper.find('select').setValue('Failed')
    await flushPromises()
    expect(cardSetApi.sends).toHaveBeenLastCalledWith('NeedsAttention', false)

    vi.mocked(cardSetApi.sends).mockClear()
    await wrapper.find('select').setValue('Cancelled')
    await flushPromises()
    expect(cardSetApi.sends).not.toHaveBeenCalled()
    expect(wrapper.find('[data-test="card-sets"]').exists()).toBe(false)
  })

  it('asks for every set when the filter is cleared', async () => {
    await mountWith(null, [])

    expect(cardSetApi.sends).toHaveBeenCalledWith(undefined, false)
  })

  it('offers the older complete sets it left out, and fetches them on request', async () => {
    const wrapper = await mountWith(null, [cardSet('Sending')], 3)

    const toggle = wrapper.find('[data-test="older-card-sets"]')
    expect(toggle.text()).toBe('Show 3 older complete sets')

    await toggle.trigger('click')
    await flushPromises()

    expect(cardSetApi.sends).toHaveBeenLastCalledWith(undefined, true)
    expect(wrapper.find('[data-test="older-card-sets"]').text()).toBe('Hide older complete sets')
  })

  it('keeps the section, not the empty message, when only older sets exist', async () => {
    const wrapper = await mountWith(null, [], 1)

    expect(wrapper.find('[data-test="older-card-sets"]').text()).toBe('Show 1 older complete set')
    expect(wrapper.text()).not.toContain('No transfers yet')
  })

  it('has no older-sets toggle when nothing was left out', async () => {
    const wrapper = await mountWith(null, [cardSet('Complete')])

    expect(wrapper.find('[data-test="older-card-sets"]').exists()).toBe(false)
  })
})
