<script setup lang="ts">
import { computed, onBeforeUnmount, watch } from 'vue'
import { useCardSetStore } from '@/stores/cardSetStore'
import { useConnectionStore } from '@/stores/connectionStore'
import { useConfirm } from '@/composables/useConfirm'
import type { CardSetBook, CardSetCard, PackingMode } from '@/types'

export interface SeriesBookOption {
  id: string
  title: string
  sequence: string | null
}

const props = defineProps<{
  seriesId: string
  libraryId: string
  /** Every book of the series, so excluded ones can be put back. */
  seriesBooks: SeriesBookOption[]
}>()

const store = useCardSetStore()
const connectionStore = useConnectionStore()
const { confirm } = useConfirm()

watch(
  () => [props.seriesId, props.libraryId] as const,
  ([series, library]) => store.load(series, library),
  { immediate: true },
)
onBeforeUnmount(() => store.stopPolling())

const MIB = 1024 * 1024
const modes: PackingMode[] = ['Balance', 'Fill']

/** Books sharing a sequence number are usually two editions of one book. */
const duplicateSequences = computed(() => {
  const counts = new Map<string, number>()
  for (const b of props.seriesBooks) {
    if (b.sequence) counts.set(b.sequence, (counts.get(b.sequence) ?? 0) + 1)
  }
  return new Set([...counts].filter(([, n]) => n > 1).map(([seq]) => seq))
})

/** Per split book: its chapters, with the part each one is on. */
const splitBooks = computed(() =>
  store.books
    .map((book, bookIndex) => ({ book, bookIndex }))
    .filter(({ book }) => book.split && book.chapters)
    .map(({ book, bookIndex }) => {
      const parts = store.cards.filter(
        (c) => c.firstBookIndex === bookIndex && c.partNumber != null,
      )
      const total = book.chapters!.reduce((sum, c) => sum + c.durationSeconds, 0) || 1
      return {
        book,
        chapters: book.chapters!.map((chapter) => ({
          ...chapter,
          widthPercent: (chapter.durationSeconds / total) * 100,
          part:
            parts.find(
              (p) => chapter.index >= p.firstChapterIndex! && chapter.index <= p.lastChapterIndex!,
            )?.partNumber ?? 0,
        })),
      }
    }),
)

function booksOn(card: CardSetCard): CardSetBook[] {
  return store.books.slice(card.firstBookIndex, card.lastBookIndex + 1)
}

function mib(bytes: number) {
  return Math.round(bytes / MIB)
}

function fullPercent(card: CardSetCard) {
  return Math.min(100, (card.capacity.bytes / card.capacity.maxBytes) * 100)
}

function formatDuration(seconds: number) {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

function statusLabel(card: CardSetCard) {
  switch (card.status) {
    case 'Transferred':
      return 'On Yoto'
    case 'Transferring':
      return 'Sending…'
    case 'Failed':
      return 'Failed'
    default:
      return store.isSaved ? 'Not sent' : null
  }
}

function rename(card: CardSetCard, event: Event) {
  const name = (event.target as HTMLInputElement).value
  if (card.playlistId && name.trim() && name.trim() !== card.title)
    store.renameCard(card.playlistId, name)
}

const isBoundaryOfSplit = (index: number) =>
  store.cards[index]?.partNumber != null &&
  store.cards[index - 1]?.partNumber != null &&
  store.cards[index]!.firstBookIndex === store.cards[index - 1]!.firstBookIndex

async function deleteSet() {
  const ok = await confirm({
    title: 'Delete card set?',
    message:
      'The cards stay as playlists and nothing on Yoto changes. You can plan the series again afterwards.',
    confirmText: 'Delete set',
    variant: 'danger',
  })
  if (ok) await store.removeSet()
}
</script>

<template>
  <section class="card p-5 space-y-4" data-testid="card-plan">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h2 class="text-xl font-semibold text-gray-900">Card plan</h2>
        <p v-if="store.cards.length" class="text-sm text-gray-500">
          {{ store.cards.length }} {{ store.cards.length === 1 ? 'card' : 'cards' }}
          <template v-if="store.cardSet">
            · {{ store.cards.filter((c) => c.status === 'Transferred').length }} on Yoto
          </template>
        </p>
      </div>
      <div
        class="inline-flex rounded-lg border border-gray-200 p-0.5"
        role="group"
        aria-label="Packing"
      >
        <button
          v-for="m in modes"
          :key="m"
          type="button"
          :data-testid="`mode-${m}`"
          :aria-pressed="store.mode === m"
          :disabled="store.isSaving"
          class="px-3 py-1 text-sm rounded-md"
          :class="store.mode === m ? 'bg-yoto-blue text-white' : 'text-gray-600 hover:bg-gray-100'"
          :title="
            m === 'Balance'
              ? 'Fewest cards, evenly filled'
              : 'Fewest cards, each filled before the next'
          "
          @click="store.setMode(m)"
        >
          {{ m }}
        </button>
      </div>
    </div>

    <p v-if="store.isLoading" class="text-sm text-gray-400">Planning cards…</p>

    <p v-if="store.error" class="text-sm text-red-600" role="alert" data-testid="card-plan-error">
      {{ store.error }}
    </p>
    <ul v-if="store.messages.length" class="text-sm text-amber-700 list-disc pl-5">
      <li v-for="message in store.messages" :key="message">{{ message }}</li>
    </ul>

    <!-- Card tiles, with the break between each pair -->
    <ol class="space-y-2">
      <template v-for="(card, index) in store.cards" :key="card.playlistId ?? index">
        <li
          v-if="index > 0"
          class="flex items-center justify-center gap-2 text-xs text-gray-500"
          :data-testid="`break-${index}`"
        >
          <template v-if="isBoundaryOfSplit(index)">
            <button
              type="button"
              class="px-2 py-0.5 rounded border border-gray-200 disabled:opacity-40"
              :disabled="!store.canMoveSplit(index, -1)"
              @click="store.moveSplit(index, -1)"
            >
              ◀ One chapter earlier
            </button>
            <span>split</span>
            <button
              type="button"
              class="px-2 py-0.5 rounded border border-gray-200 disabled:opacity-40"
              :disabled="!store.canMoveSplit(index, 1)"
              @click="store.moveSplit(index, 1)"
            >
              One chapter later ▶
            </button>
          </template>
          <template
            v-else-if="card.partNumber == null && store.cards[index - 1]?.partNumber == null"
          >
            <button
              type="button"
              class="px-2 py-0.5 rounded border border-gray-200 disabled:opacity-40"
              :data-testid="`break-${index}-earlier`"
              :disabled="!store.canMoveBreak(index, -1)"
              @click="store.moveBreak(index, -1)"
            >
              ◀ One book earlier
            </button>
            <span>break</span>
            <button
              type="button"
              class="px-2 py-0.5 rounded border border-gray-200 disabled:opacity-40"
              :data-testid="`break-${index}-later`"
              :disabled="!store.canMoveBreak(index, 1)"
              @click="store.moveBreak(index, 1)"
            >
              One book later ▶
            </button>
          </template>
        </li>

        <li
          class="rounded-xl border p-4"
          :class="card.capacity.withinLimits ? 'border-gray-200' : 'border-red-300 bg-red-50'"
          :data-testid="`card-${index}`"
        >
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0 flex-1">
              <p class="text-xs uppercase tracking-wide text-gray-400">
                Card {{ index + 1 }} of {{ store.cards.length }}
              </p>
              <input
                v-if="store.isSaved && !card.locked && card.playlistId"
                :value="card.title"
                class="w-full font-medium text-gray-900 bg-transparent border-b border-transparent hover:border-gray-200 focus:border-yoto-blue focus:outline-none"
                aria-label="Card title"
                @change="rename(card, $event)"
              />
              <p v-else class="font-medium text-gray-900 truncate">{{ card.title }}</p>
            </div>
            <span
              v-if="statusLabel(card)"
              class="shrink-0 px-2 py-0.5 rounded-full text-xs font-medium"
              :class="{
                'bg-green-100 text-green-700': card.status === 'Transferred',
                'bg-blue-100 text-blue-700': card.status === 'Transferring',
                'bg-red-100 text-red-700': card.status === 'Failed',
                'bg-gray-100 text-gray-600': card.status === 'Draft',
              }"
              :data-testid="`card-${index}-status`"
            >
              {{ statusLabel(card) }}
            </span>
          </div>

          <div class="mt-3 h-2 rounded-full bg-gray-100 overflow-hidden">
            <div
              class="h-full rounded-full"
              :class="card.capacity.withinLimits ? 'bg-yoto-blue' : 'bg-red-500'"
              :style="{ width: `${fullPercent(card)}%` }"
            />
          </div>
          <p class="mt-1 text-xs text-gray-500">
            ≈ {{ mib(card.capacity.bytes) }} MB of {{ mib(card.capacity.maxBytes) }} MB ·
            {{ card.capacity.tracks }} tracks · {{ formatDuration(card.capacity.durationSeconds) }}
            <template v-if="card.partNumber != null">
              · chapters {{ card.firstChapterIndex! + 1 }}–{{ card.lastChapterIndex! + 1 }}
            </template>
          </p>

          <ul class="mt-2 text-sm text-gray-700 space-y-0.5">
            <li v-for="book in booksOn(card)" :key="book.absLibraryItemId" class="truncate">
              <span v-if="book.sequence != null" class="text-gray-400">#{{ book.sequence }}</span>
              {{ book.title }}
            </li>
          </ul>

          <p v-if="card.errorMessage" class="mt-2 text-xs text-red-600">{{ card.errorMessage }}</p>
          <button
            v-if="card.status === 'Failed' && card.playlistId"
            type="button"
            class="btn-secondary mt-2 text-sm"
            :disabled="store.isSaving"
            @click="store.send([card.playlistId])"
          >
            Retry this card
          </button>
        </li>
      </template>
    </ol>

    <!-- Split editor: where each split book's chapters fall -->
    <div v-for="split in splitBooks" :key="split.book.absLibraryItemId" class="space-y-1">
      <p class="text-sm font-medium text-gray-700">{{ split.book.title }}: chapters by card</p>
      <div class="flex h-6 rounded overflow-hidden border border-gray-200">
        <div
          v-for="chapter in split.chapters"
          :key="chapter.index"
          class="h-full border-r border-white/60 last:border-r-0"
          :class="chapter.part % 2 === 1 ? 'bg-yoto-blue/70' : 'bg-yoto-orange/70'"
          :style="{ width: `${chapter.widthPercent}%` }"
          :title="`${chapter.title} · part ${chapter.part}`"
        />
      </div>
      <p class="text-xs text-gray-500">
        Move a split with the chapter buttons between its cards; splits only fall between chapters.
      </p>
    </div>

    <!-- Books in or out of the plan -->
    <details class="text-sm">
      <summary class="cursor-pointer text-gray-600">Books in the plan</summary>
      <ul class="mt-2 space-y-1">
        <li v-for="book in seriesBooks" :key="book.id" class="flex items-center gap-2">
          <input
            :id="`include-${book.id}`"
            type="checkbox"
            :checked="!store.excluded.includes(book.id)"
            :disabled="store.isSaving"
            @change="store.toggleExcluded(book.id)"
          />
          <label :for="`include-${book.id}`" class="text-gray-700">
            <span v-if="book.sequence" class="text-gray-400">#{{ book.sequence }}</span>
            {{ book.title }}
          </label>
          <span
            v-if="book.sequence && duplicateSequences.has(book.sequence)"
            class="text-xs text-amber-600"
          >
            duplicate?
          </span>
        </li>
      </ul>
    </details>

    <div class="flex flex-wrap items-center gap-3 pt-2 border-t border-gray-100">
      <button
        v-if="!store.isSaved"
        type="button"
        class="btn-primary"
        data-testid="make-cards"
        :disabled="
          !store.cards.length ||
          !store.withinLimits ||
          store.isSaving ||
          !connectionStore.isYotoConnected
        "
        @click="store.makeCards()"
      >
        {{
          store.isSaving
            ? 'Making cards…'
            : `Make ${store.cards.length} ${store.cards.length === 1 ? 'card' : 'cards'}`
        }}
      </button>
      <template v-else>
        <button
          type="button"
          class="btn-primary"
          data-testid="send-cards"
          :disabled="
            !store.unsentCards.length ||
            store.isSending ||
            store.isSaving ||
            !connectionStore.isYotoConnected
          "
          @click="store.send()"
        >
          {{
            store.isSending
              ? 'Sending…'
              : store.unsentCards.length
                ? `Send ${store.unsentCards.length} ${store.unsentCards.length === 1 ? 'card' : 'cards'}`
                : 'All cards on Yoto'
          }}
        </button>
        <button type="button" class="btn-secondary" :disabled="store.isSaving" @click="deleteSet">
          Delete set
        </button>
      </template>
      <slot name="secondary-actions" />
    </div>
  </section>
</template>
