<script setup lang="ts">
import { onMounted, ref, computed, watch } from 'vue'
import { useLibraryStore } from '@/stores/libraryStore'
import { useConnectionStore } from '@/stores/connectionStore'
import { useSignalR } from '@/composables/useSignalR'
import { useToast } from '@/composables/useToast'
import { useConfirm } from '@/composables/useConfirm'
import { transferApi } from '@/services/api'
import type { TransferProgressUpdate } from '@/types'

const props = defineProps<{ itemId: string }>()

const libraryStore = useLibraryStore()
const connectionStore = useConnectionStore()
const { connect, joinTransfer, progressUpdates } = useSignalR()
const toast = useToast()
const { confirm } = useConfirm()

const overrideMinAge = ref<number | null>(null)
const overrideMaxAge = ref<number | null>(null)
const isTransferring = ref(false)
const activeTransferId = ref<string | null>(null)

const book = computed(() => libraryStore.currentBookDetail)
const metadata = computed(() => book.value?.item?.media?.metadata)
const ageSuggestion = computed(() => book.value?.ageSuggestion)

const effectiveMinAge = computed(() => overrideMinAge.value ?? ageSuggestion.value?.suggestedMinAge ?? 5)
const effectiveMaxAge = computed(() => overrideMaxAge.value ?? ageSuggestion.value?.suggestedMaxAge ?? 10)
const isOverridden = computed(() => overrideMinAge.value !== null || overrideMaxAge.value !== null)

// Live progress from SignalR
const liveProgress = computed<TransferProgressUpdate | null>(() => {
  if (!activeTransferId.value) return null
  return progressUpdates.value.get(activeTransferId.value) ?? null
})

onMounted(async () => {
  await libraryStore.loadBookDetail(props.itemId)
  // If there's an active existing transfer, subscribe for updates
  if (book.value?.existingTransfer && !['Completed', 'Failed', 'Cancelled'].includes(book.value.existingTransfer.status)) {
    activeTransferId.value = book.value.existingTransfer.id
    await connect()
    await joinTransfer(book.value.existingTransfer.id)
  }
})

function formatDuration(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

function resetAgeOverride() {
  overrideMinAge.value = null
  overrideMaxAge.value = null
}

function onCoverError(e: Event) {
  const img = e.target as HTMLImageElement
  img.src = 'data:image/svg+xml,' + encodeURIComponent(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 300" fill="none">
      <rect width="200" height="300" rx="8" fill="#E5E7EB"/>
      <path d="M80 120h40v8H80v-8zm-8 24h56v6H72v-6zm0 16h56v6H72v-6zm0 16h40v6H72v-6z" fill="#9CA3AF"/>
      <rect x="76" y="80" width="48" height="24" rx="4" fill="#9CA3AF" opacity="0.5"/>
    </svg>
  `)
}

async function startTransfer() {
  if (!connectionStore.userConnectionId) return

  const ok = await confirm({
    title: 'Transfer to Yoto?',
    message: `This will create a Yoto MYO card for "${metadata.value?.title}". The audio will be downloaded, uploaded, and transcoded.`,
    confirmText: 'Start Transfer',
  })
  if (!ok) return

  isTransferring.value = true

  try {
    const { data } = await transferApi.transferBook(connectionStore.userConnectionId, {
      absLibraryItemId: props.itemId,
      overrideMinAge: overrideMinAge.value ?? undefined,
      overrideMaxAge: overrideMaxAge.value ?? undefined,
    })
    toast.success('Transfer queued successfully!')

    // Subscribe to SignalR for live progress
    activeTransferId.value = data.transferId ?? null
    if (activeTransferId.value) {
      await connect()
      await joinTransfer(activeTransferId.value)
    }
  } catch (err: any) {
    toast.error(`Transfer failed: ${err.response?.data?.message ?? err.message}`)
  } finally {
    isTransferring.value = false
  }
}
</script>

<template>
  <div v-if="libraryStore.isLoading" class="text-center py-12 text-gray-400">Loading...</div>

  <div v-else-if="book" class="space-y-6">
    <!-- Book header -->
    <div class="flex space-x-6">
      <img
        :src="libraryStore.getCoverUrl(props.itemId)"
        :alt="metadata?.title ?? 'Cover'"
        class="w-48 h-72 object-cover rounded-xl shadow-md bg-gray-100"
        @error="onCoverError"
      />
      <div class="flex-1 space-y-3">
        <h1 class="text-3xl font-bold text-gray-900">{{ metadata?.title }}</h1>
        <p v-if="metadata?.subtitle" class="text-lg text-gray-500">{{ metadata.subtitle }}</p>
        <p class="text-gray-600">
          by {{ metadata?.authors?.map(a => a.name).join(', ') ?? 'Unknown' }}
        </p>
        <p v-if="metadata?.narrators?.length" class="text-sm text-gray-500">
          Narrated by {{ metadata.narrators.join(', ') }}
        </p>
        <div class="flex flex-wrap gap-2 mt-2">
          <span
            v-for="genre in metadata?.genres ?? []"
            :key="genre"
            class="px-2 py-1 bg-gray-100 text-gray-600 rounded-full text-xs"
          >
            {{ genre }}
          </span>
        </div>
        <div class="flex items-center space-x-4 text-sm text-gray-500 pt-2">
          <span>{{ formatDuration(book.item.media?.duration ?? 0) }}</span>
          <span>{{ book.item.media?.numChapters ?? 0 }} chapters</span>
          <span>{{ book.item.media?.numAudioFiles ?? 0 }} files</span>
        </div>

        <!-- Series info -->
        <div v-if="metadata?.series?.length" class="flex items-center space-x-2 pt-1">
          <span
            v-for="s in metadata.series"
            :key="s.id"
            class="px-3 py-1 bg-yoto-blue/10 text-yoto-blue rounded-full text-sm font-medium"
          >
            {{ s.name }} {{ s.sequence ? `#${s.sequence}` : '' }}
          </span>
        </div>

        <!-- Existing transfer badge -->
        <div v-if="book.existingTransfer" class="mt-2">
          <span
            class="px-3 py-1 rounded-full text-sm font-medium"
            :class="{
              'bg-green-100 text-green-700': book.existingTransfer.status === 'Completed',
              'bg-yellow-100 text-yellow-700': !['Completed', 'Failed'].includes(book.existingTransfer.status),
              'bg-red-100 text-red-700': book.existingTransfer.status === 'Failed',
            }"
          >
            Previously transferred: {{ book.existingTransfer.status }}
          </span>
        </div>
      </div>
    </div>

    <!-- Age Range Section -->
    <div class="card">
      <h2 class="text-lg font-semibold mb-4">Age Range</h2>

      <div v-if="ageSuggestion" class="space-y-4">
        <div class="flex items-center justify-between">
          <div>
            <p class="text-sm text-gray-600">
              <span class="font-medium">Suggested:</span> {{ ageSuggestion.suggestedMinAge }}–{{ ageSuggestion.suggestedMaxAge }} years
            </p>
            <p class="text-xs text-gray-400 mt-1">{{ ageSuggestion.reason }}</p>
          </div>
          <button v-if="isOverridden" @click="resetAgeOverride" class="text-sm text-yoto-blue hover:underline">
            Reset to suggested
          </button>
        </div>

        <!-- Signals breakdown -->
        <div v-if="ageSuggestion.signals.length" class="flex flex-wrap gap-2">
          <span
            v-for="signal in ageSuggestion.signals"
            :key="`${signal.signal}-${signal.value}`"
            class="px-2 py-1 bg-gray-50 text-gray-500 rounded text-xs"
          >
            {{ signal.signal }}: {{ signal.value }} ({{ signal.weight }}%)
          </span>
        </div>

        <!-- Override sliders -->
        <div class="grid grid-cols-2 gap-6 pt-2">
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              Min Age: <strong>{{ effectiveMinAge }}</strong>
            </label>
            <input
              type="range"
              min="0"
              max="18"
              :value="effectiveMinAge"
              @input="overrideMinAge = Number(($event.target as HTMLInputElement).value)"
              class="w-full accent-yoto-blue"
            />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">
              Max Age: <strong>{{ effectiveMaxAge }}</strong>
            </label>
            <input
              type="range"
              min="0"
              max="18"
              :value="effectiveMaxAge"
              @input="overrideMaxAge = Number(($event.target as HTMLInputElement).value)"
              class="w-full accent-yoto-blue"
            />
          </div>
        </div>

        <p v-if="isOverridden" class="text-xs text-yoto-orange">
          Age range overridden from suggested {{ ageSuggestion.suggestedMinAge }}–{{ ageSuggestion.suggestedMaxAge }}
        </p>
      </div>
    </div>

    <!-- Chapters -->
    <div class="card">
      <h2 class="text-lg font-semibold mb-4">
        Chapters ({{ book.item.media?.chapters?.length ?? 0 }})
      </h2>
      <div class="divide-y divide-gray-100">
        <div
          v-for="(chapter, i) in book.item.media?.chapters ?? []"
          :key="chapter.id"
          class="py-3 flex items-center justify-between"
        >
          <div class="flex items-center space-x-3">
            <span class="text-xs text-gray-400 w-6 text-right">{{ i + 1 }}</span>
            <span class="text-sm text-gray-800">{{ chapter.title }}</span>
          </div>
          <span class="text-xs text-gray-400">
            {{ formatDuration(chapter.end - chapter.start) }}
          </span>
        </div>
      </div>
    </div>

    <!-- Live transfer progress (Phase 6: SignalR wired) -->
    <div v-if="liveProgress" class="card">
      <div class="flex items-center justify-between mb-2">
        <h3 class="text-sm font-medium text-gray-700">Transfer Progress</h3>
        <span class="text-xs px-2 py-0.5 rounded-full bg-blue-100 text-blue-700">{{ liveProgress.status }}</span>
      </div>
      <div class="w-full bg-gray-100 rounded-full h-2.5">
        <div
          class="bg-yoto-blue h-2.5 rounded-full transition-all duration-300"
          :style="{ width: `${liveProgress.progressPercent}%` }"
        />
      </div>
      <p class="text-xs text-gray-500 mt-1">
        {{ liveProgress.currentStep ?? `${liveProgress.progressPercent}% complete` }}
      </p>
    </div>

    <!-- Transfer button -->
    <div class="sticky bottom-4">
      <button
        @click="startTransfer"
        :disabled="isTransferring || !!activeTransferId || !connectionStore.isYotoConnected"
        class="btn-primary w-full text-lg py-3 shadow-lg"
      >
        <template v-if="isTransferring">Queueing Transfer...</template>
        <template v-else-if="activeTransferId">Transfer In Progress...</template>
        <template v-else-if="!connectionStore.isYotoConnected">Connect Yoto to Transfer</template>
        <template v-else-if="book.existingTransfer?.status === 'Completed'">Re-transfer to Yoto</template>
        <template v-else>Transfer to Yoto Card</template>
      </button>
    </div>
  </div>
</template>
