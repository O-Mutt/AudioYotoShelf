<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useConnectionStore } from '@/stores/connectionStore'
import { useToast } from '@/composables/useToast'
import { useConfirm } from '@/composables/useConfirm'
import { cardsApi } from '@/services/api'
import type { YotoCardSummary, YotoCardDetail } from '@/types'

const connectionStore = useConnectionStore()
const toast = useToast()
const { confirm } = useConfirm()

const cards = ref<YotoCardSummary[]>([])
const isLoading = ref(true)
const yotoSessionExpired = ref(false)
const expandedCardId = ref<string | null>(null)
const expandedDetail = ref<YotoCardDetail | null>(null)
const isLoadingDetail = ref(false)

onMounted(async () => {
  await loadCards()
})

async function loadCards() {
  if (!connectionStore.userConnectionId) return
  isLoading.value = true
  yotoSessionExpired.value = false
  try {
    const { data } = await cardsApi.getCards(connectionStore.userConnectionId)
    cards.value = data
  } catch (err: any) {
    if (err?.response?.status === 401) {
      yotoSessionExpired.value = true
    } else {
      toast.error('Failed to load Yoto cards')
    }
  } finally {
    isLoading.value = false
  }
}

async function toggleExpand(cardId: string) {
  if (expandedCardId.value === cardId) {
    expandedCardId.value = null
    expandedDetail.value = null
    return
  }
  expandedCardId.value = cardId
  expandedDetail.value = null
  isLoadingDetail.value = true
  try {
    const { data } = await cardsApi.getCard(connectionStore.userConnectionId!, cardId)
    expandedDetail.value = data
  } catch {
    toast.error('Failed to load card details')
  } finally {
    isLoadingDetail.value = false
  }
}

async function handleDelete(cardId: string, title: string) {
  const ok = await confirm({
    title: 'Delete Yoto Card',
    message: `Are you sure you want to delete "${title || 'this card'}"? This cannot be undone.`,
    confirmText: 'Delete',
    variant: 'danger',
  })
  if (!ok) return

  try {
    await cardsApi.deleteCard(connectionStore.userConnectionId!, cardId)
    cards.value = cards.value.filter(c => c.cardId !== cardId)
    if (expandedCardId.value === cardId) {
      expandedCardId.value = null
      expandedDetail.value = null
    }
    toast.success('Card deleted')
  } catch {
    toast.error('Failed to delete card')
  }
}

function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}:${s.toString().padStart(2, '0')}`
}
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between">
      <h1 class="text-2xl font-bold text-gray-900">My Yoto Cards</h1>
      <span v-if="!isLoading" class="text-sm text-gray-400">
        {{ cards.length }} card{{ cards.length !== 1 ? 's' : '' }}
      </span>
    </div>

    <!-- Not connected -->
    <div v-if="!connectionStore.isYotoConnected" class="text-center py-16">
      <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-16 w-16 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
      </svg>
      <h3 class="mt-4 text-lg font-medium text-gray-700">Yoto not connected</h3>
      <p class="mt-2 text-sm text-gray-400">Connect your Yoto account in Settings to manage your cards.</p>
      <router-link to="/setup" class="mt-4 inline-block btn-primary text-sm">Connect Yoto</router-link>
    </div>

    <!-- Session expired -->
    <div v-else-if="yotoSessionExpired" class="text-center py-16">
      <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-16 w-16 text-yellow-400" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" d="M16.5 10.5V6.75a4.5 4.5 0 10-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H6.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z" />
      </svg>
      <h3 class="mt-4 text-lg font-medium text-gray-700">Yoto session expired</h3>
      <p class="mt-2 text-sm text-gray-400">Your Yoto connection needs to be refreshed.</p>
      <router-link to="/settings" class="mt-4 inline-block btn-primary text-sm">Reconnect Yoto</router-link>
    </div>

    <!-- Loading -->
    <div v-else-if="isLoading" class="text-center py-16">
      <div class="inline-block h-8 w-8 animate-spin rounded-full border-4 border-yoto-blue border-r-transparent" />
      <p class="mt-3 text-sm text-gray-400">Loading your Yoto cards...</p>
    </div>

    <!-- Empty -->
    <div v-else-if="cards.length === 0" class="text-center py-16">
      <svg xmlns="http://www.w3.org/2000/svg" class="mx-auto h-16 w-16 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" d="M2.25 8.25h19.5M2.25 9h19.5m-16.5 5.25h6m-6 2.25h3m-3.75 3h15a2.25 2.25 0 002.25-2.25V6.75A2.25 2.25 0 0019.5 4.5h-15a2.25 2.25 0 00-2.25 2.25v10.5A2.25 2.25 0 004.5 19.5z" />
      </svg>
      <h3 class="mt-4 text-lg font-medium text-gray-700">No MYO cards yet</h3>
      <p class="mt-2 text-sm text-gray-400">Transfer books from your library to create Yoto cards.</p>
      <router-link to="/library" class="mt-4 inline-block btn-primary text-sm">Browse Library</router-link>
    </div>

    <!-- Cards List -->
    <div v-else class="space-y-3">
      <div
        v-for="card in cards"
        :key="card.cardId"
        class="card p-0 overflow-hidden"
      >
        <!-- Card header row -->
        <div
          @click="toggleExpand(card.cardId)"
          class="p-4 cursor-pointer hover:bg-gray-50 transition-colors flex items-center gap-4"
        >
          <!-- Cover thumbnail -->
          <div class="w-12 h-16 rounded bg-gray-100 flex-shrink-0 overflow-hidden">
            <img
              v-if="card.metadata?.cover?.imageL"
              :src="card.metadata.cover.imageL"
              class="w-full h-full object-cover"
            />
            <div v-else class="w-full h-full flex items-center justify-center text-gray-400">
              <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25" />
              </svg>
            </div>
          </div>

          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2">
              <h3 class="font-medium text-gray-900 truncate">
                {{ card.sourceBookTitle || card.metadata?.description || `Card ${card.cardId.slice(0, 8)}` }}
              </h3>
              <span
                v-if="card.fromAudioYotoShelf"
                class="px-2 py-0.5 bg-yoto-blue/10 text-yoto-blue rounded-full text-xs font-medium flex-shrink-0"
              >
                AudioYotoShelf
              </span>
            </div>
            <div class="flex items-center gap-3 text-xs text-gray-500 mt-1">
              <span v-if="card.metadata?.author">{{ card.metadata.author }}</span>
              <span>{{ card.chapterCount }} ch.</span>
              <span>{{ card.trackCount }} tracks</span>
              <span v-if="card.metadata?.minAge != null">
                Ages {{ card.metadata.minAge }}–{{ card.metadata.maxAge ?? '18' }}
              </span>
              <span v-if="card.metadata?.category" class="capitalize">{{ card.metadata.category }}</span>
            </div>
          </div>

          <!-- Expand arrow -->
          <svg
            xmlns="http://www.w3.org/2000/svg" class="h-5 w-5 text-gray-400 transition-transform flex-shrink-0"
            :class="{ 'rotate-90': expandedCardId === card.cardId }"
            viewBox="0 0 20 20" fill="currentColor"
          >
            <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd" />
          </svg>
        </div>

        <!-- Expanded detail -->
        <Transition
          enter-active-class="transition-all duration-200 ease-out"
          leave-active-class="transition-all duration-150 ease-in"
          enter-from-class="max-h-0 opacity-0"
          leave-to-class="max-h-0 opacity-0"
        >
          <div v-if="expandedCardId === card.cardId" class="border-t border-gray-100 overflow-hidden">
            <div v-if="isLoadingDetail" class="p-6 text-center text-sm text-gray-400">Loading details...</div>
            <div v-else-if="expandedDetail" class="p-4 space-y-3">
              <!-- Chapters -->
              <div
                v-for="chapter in expandedDetail.content?.chapters ?? []"
                :key="chapter.key"
                class="bg-gray-50 rounded-lg p-3"
              >
                <div class="flex items-center justify-between">
                  <div class="flex items-center gap-2">
                    <img
                      v-if="chapter.display?.icon16x16"
                      :src="chapter.display.icon16x16"
                      class="w-4 h-4 image-pixelated"
                      alt="icon"
                    />
                    <span class="text-sm font-medium text-gray-800">{{ chapter.title }}</span>
                  </div>
                  <span class="text-xs text-gray-400">{{ chapter.tracks?.length ?? 0 }} tracks</span>
                </div>
                <div v-if="chapter.tracks?.length" class="mt-2 space-y-1 pl-6">
                  <div
                    v-for="track in chapter.tracks"
                    :key="track.key"
                    class="flex items-center justify-between text-xs text-gray-500"
                  >
                    <span class="truncate">{{ track.title }}</span>
                    <span v-if="track.duration" class="flex-shrink-0 ml-2">{{ formatDuration(track.duration) }}</span>
                  </div>
                </div>
              </div>

              <!-- Delete button -->
              <div class="pt-2 border-t border-gray-100">
                <button
                  @click.stop="handleDelete(card.cardId, card.metadata?.description || card.sourceBookTitle || '')"
                  class="text-sm text-red-600 hover:text-red-700 font-medium"
                >
                  Delete Card
                </button>
              </div>
            </div>
          </div>
        </Transition>
      </div>
    </div>
  </div>
</template>

<style scoped>
.image-pixelated {
  image-rendering: pixelated;
}
</style>
