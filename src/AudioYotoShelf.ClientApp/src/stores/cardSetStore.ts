import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { isAxiosError } from 'axios'
import { cardSetApi } from '@/services/api'
import { errorMessage } from '@/utils/errors'
import type {
  CardSetBook,
  CardSetCard,
  CardSetLayout,
  CardSetPlanResponse,
  CardSetResponse,
  PackingMode,
} from '@/types'

const POLL_MS = 5000

/** Cards the server has queued or is sending; the set is re-read until none are left. */
const inFlight = (card: CardSetCard) => card.status === 'Queued' || card.status === 'Transferring'

/** The breaks and split points that produce these cards (spec §5.5). */
export function layoutOf(cards: CardSetCard[], books: CardSetBook[]): CardSetLayout {
  const breaks: number[] = []
  const splitPoints: Record<string, number[]> = {}
  cards.forEach((card, i) => {
    if (card.partNumber != null && card.partNumber > 1 && card.firstChapterIndex != null) {
      const id = books[card.firstBookIndex]!.absLibraryItemId
      ;(splitPoints[id] ??= []).push(card.firstChapterIndex)
    } else if (i > 0) {
      breaks.push(card.firstBookIndex)
    }
  })
  return { breaks, splitPoints }
}

const isPart = (card: CardSetCard | undefined) => card?.partNumber != null
const bookCount = (card: CardSetCard) => card.lastBookIndex - card.firstBookIndex + 1
const chapterCount = (card: CardSetCard) =>
  (card.lastChapterIndex ?? 0) - (card.firstChapterIndex ?? 0) + 1

/**
 * One series' card plan: a preview until the user makes the cards, then the saved set. Moving a
 * break or split point re-plans on the server; a move that would overflow a card is refused and
 * the cards stay as they were, with the reason in `error`.
 */
export const useCardSetStore = defineStore('cardSet', () => {
  const seriesId = ref<string | null>(null)
  const libraryId = ref<string | null>(null)
  const cardSet = ref<CardSetResponse | null>(null)
  const preview = ref<CardSetPlanResponse | null>(null)
  const mode = ref<PackingMode>('Balance')
  const excluded = ref<string[]>([])
  /** User-chosen layout for the preview; null lets the server pack automatically. */
  const layout = ref<CardSetLayout | null>(null)
  const isLoading = ref(false)
  const isSaving = ref(false)
  const error = ref<string | null>(null)
  let pollTimer: ReturnType<typeof setInterval> | null = null

  const view = computed(() => cardSet.value ?? preview.value)
  const books = computed(() => view.value?.books ?? [])
  const cards = computed(() => view.value?.cards ?? [])
  const withinLimits = computed(() => view.value?.withinLimits ?? false)
  const messages = computed(() => view.value?.messages ?? [])
  const isSaved = computed(() => cardSet.value !== null)
  const unsentCards = computed(() => cards.value.filter((c) => !c.locked))
  const isSending = computed(() => cards.value.some(inFlight))
  /** Books the planner had to leave out because one chapter is bigger than a card. */
  const unsplittable = computed(() => preview.value?.unsplittableBookIds ?? [])

  async function load(series: string, library: string) {
    stopPolling()
    seriesId.value = series
    libraryId.value = library
    cardSet.value = null
    preview.value = null
    layout.value = null
    error.value = null
    isLoading.value = true
    try {
      const { data: sets } = await cardSetApi.list(series)
      const existing = sets[0]
      if (existing) {
        const { data } = await cardSetApi.get(existing.id)
        setSaved(data)
        if (data.cards.some(inFlight)) startPolling()
      } else {
        await replan()
      }
    } catch (err) {
      error.value = errorMessage(err)
    } finally {
      isLoading.value = false
    }
  }

  function setSaved(data: CardSetResponse) {
    cardSet.value = data
    mode.value = data.packingMode
    excluded.value = [...data.excludedItemIds]
  }

  async function replan() {
    if (!seriesId.value || !libraryId.value) return
    const { data } = await cardSetApi.plan(planRequest(layout.value))
    preview.value = data
  }

  function planRequest(withLayout: CardSetLayout | null) {
    return {
      absSeriesId: seriesId.value!,
      absLibraryId: libraryId.value!,
      mode: mode.value,
      excludedItemIds: excluded.value,
      breaks: withLayout?.breaks ?? null,
      splitPoints: withLayout?.splitPoints ?? null,
    }
  }

  async function run(action: () => Promise<void>) {
    error.value = null
    isSaving.value = true
    try {
      await action()
    } catch (err) {
      error.value = refusal(err)
    } finally {
      isSaving.value = false
    }
  }

  /** A refused plan answers 422 with the plan; its messages say which card overflowed. */
  function refusal(err: unknown): string {
    if (isAxiosError(err)) {
      const planMessages = (err.response?.data as { plan?: { messages?: string[] } })?.plan
        ?.messages
      if (planMessages?.length) return planMessages.join(' ')
    }
    return errorMessage(err)
  }

  function setMode(next: PackingMode) {
    if (next === mode.value) return Promise.resolve()
    return run(async () => {
      if (cardSet.value) {
        const { data } = await cardSetApi.update(cardSet.value.id, { packingMode: next })
        setSaved(data)
      } else {
        mode.value = next
        layout.value = null
        await replan()
      }
    })
  }

  function toggleExcluded(bookId: string) {
    const next = excluded.value.includes(bookId)
      ? excluded.value.filter((id) => id !== bookId)
      : [...excluded.value, bookId]
    return run(async () => {
      if (cardSet.value) {
        const { data } = await cardSetApi.update(cardSet.value.id, { excludedItemIds: next })
        setSaved(data)
      } else {
        excluded.value = next
        layout.value = null
        await replan()
      }
    })
  }

  function applyLayout(next: CardSetLayout) {
    return run(async () => {
      if (cardSet.value) {
        const { data } = await cardSetApi.updateBreaks(
          cardSet.value.id,
          next.breaks,
          next.splitPoints,
        )
        setSaved(data)
        return
      }
      const { data } = await cardSetApi.plan(planRequest(next))
      if (!data.withinLimits) {
        error.value = data.messages.join(' ') || 'A card would not fit.'
        return
      }
      preview.value = data
      layout.value = next
    })
  }

  /** Whether the break in front of card `index` can move one book earlier (-1) or later (+1). */
  function canMoveBreak(index: number, direction: -1 | 1): boolean {
    const before = cards.value[index - 1]
    const after = cards.value[index]
    if (!before || !after || isPart(before) || isPart(after)) return false
    if (before.locked || after.locked || isSaving.value) return false
    return direction === -1 ? bookCount(before) > 1 : bookCount(after) > 1
  }

  function moveBreak(index: number, direction: -1 | 1) {
    if (!canMoveBreak(index, direction)) return Promise.resolve()
    const current = layoutOf(cards.value, books.value)
    const at = cards.value[index]!.firstBookIndex
    const breaks = current.breaks.map((b) => (b === at ? b + direction : b))
    return applyLayout({ ...current, breaks })
  }

  /** Whether the split in front of part card `index` can move one chapter earlier or later. */
  function canMoveSplit(index: number, direction: -1 | 1): boolean {
    const before = cards.value[index - 1]
    const after = cards.value[index]
    if (!before || !after || !isPart(before) || !isPart(after)) return false
    if (before.firstBookIndex !== after.firstBookIndex) return false
    if (before.locked || after.locked || isSaving.value) return false
    return direction === -1 ? chapterCount(before) > 1 : chapterCount(after) > 1
  }

  function moveSplit(index: number, direction: -1 | 1) {
    if (!canMoveSplit(index, direction)) return Promise.resolve()
    const current = layoutOf(cards.value, books.value)
    const card = cards.value[index]!
    const id = books.value[card.firstBookIndex]!.absLibraryItemId
    const points = (current.splitPoints[id] ?? []).map((p) =>
      p === card.firstChapterIndex ? p + direction : p,
    )
    return applyLayout({ ...current, splitPoints: { ...current.splitPoints, [id]: points } })
  }

  /** Saves the plan as a set and sends every card. */
  function makeCards() {
    return run(async () => {
      const { data } = await cardSetApi.create(planRequest(layout.value))
      setSaved(data)
      preview.value = null
      layout.value = null
      await sendNow()
    })
  }

  function send(playlistIds?: string[]) {
    return run(() => sendNow(playlistIds))
  }

  async function sendNow(playlistIds?: string[]) {
    if (!cardSet.value) return
    const { data } = await cardSetApi.transfer(cardSet.value.id, playlistIds)
    // The server marks the cards Queued before it answers, so re-reading now shows them as queued
    // (a retried card stops reading Failed) and polling runs until they have all landed.
    if (data.playlistIds?.length) {
      await refresh()
      startPolling()
    }
  }

  function renameCard(playlistId: string, name: string) {
    return run(async () => {
      if (!cardSet.value || !name.trim()) return
      const { data } = await cardSetApi.renameCard(cardSet.value.id, playlistId, name.trim())
      setSaved(data)
    })
  }

  function removeSet() {
    return run(async () => {
      if (!cardSet.value) return
      await cardSetApi.remove(cardSet.value.id)
      stopPolling()
      cardSet.value = null
      await replan()
    })
  }

  /** Re-reads the set while any card is queued or being sent. */
  function startPolling() {
    if (pollTimer || !cards.value.some(inFlight)) return
    pollTimer = setInterval(refresh, POLL_MS)
  }

  async function refresh() {
    if (!cardSet.value) return stopPolling()
    try {
      const { data } = await cardSetApi.get(cardSet.value.id)
      setSaved(data)
      if (!data.cards.some(inFlight)) stopPolling()
    } catch (err) {
      error.value = errorMessage(err)
      stopPolling()
    }
  }

  function stopPolling() {
    if (pollTimer) clearInterval(pollTimer)
    pollTimer = null
  }

  return {
    cardSet,
    preview,
    mode,
    excluded,
    layout,
    isLoading,
    isSaving,
    error,
    books,
    cards,
    withinLimits,
    messages,
    isSaved,
    unsentCards,
    isSending,
    unsplittable,
    load,
    setMode,
    toggleExcluded,
    canMoveBreak,
    moveBreak,
    canMoveSplit,
    moveSplit,
    makeCards,
    send,
    renameCard,
    removeSet,
    refresh,
    stopPolling,
  }
})
