import axios from 'axios'
import type {
  AbsConnectOptions,
  AbsConnectRequest,
  AbsConnectResponse,
  AbsLibrary,
  AbsLibraryItemsResponse,
  AbsSeriesItem,
  AbsSeriesResponse,
  BatchTransferResponse,
  BookDetailResponse,
  CardLimits,
  CardSetPlanResponse,
  CardSetResponse,
  CardSetSummary,
  ConnectionStatus,
  PackingMode,
  SeriesCardStatus,
  TransferResponse,
} from '@/types'

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  // Send the http-only session cookie with every request (and accept Set-Cookie on login).
  // The caller's connection id rides in the cookie, so it never appears in URLs.
  withCredentials: true,
})

// --- Auth ---

export const authApi = {
  getAbsConnectOptions() {
    return api.get<AbsConnectOptions>('/auth/abs/options')
  },

  connectAbs(request: AbsConnectRequest) {
    return api.post<AbsConnectResponse>('/auth/abs/connect', request)
  },

  logout() {
    return api.post('/auth/logout')
  },

  validateAbsToken() {
    return api.post<{ valid: boolean }>('/auth/abs/validate')
  },

  getYotoAuthUrl() {
    return api.get<{ authUrl: string }>('/auth/yoto/authorize')
  },

  getConnectionStatus() {
    return api.get<ConnectionStatus>('/auth/status')
  },

  // Phase 3: Settings save
  updateSettings(settings: {
    defaultLibraryId?: string
    defaultMinAge?: number
    defaultMaxAge?: number
  }) {
    return api.patch<ConnectionStatus>('/auth/settings', settings)
  },
}

// --- Libraries ---

export const libraryApi = {
  getLibraries() {
    return api.get<AbsLibrary[]>('/libraries')
  },

  getLibraryItems(
    libraryId: string,
    page = 0,
    limit = 20,
    collapseSeries = false,
    search?: string,
    sort?: string,
    sortDesc = false,
  ) {
    return api.get<AbsLibraryItemsResponse>(`/libraries/library/${libraryId}/items`, {
      params: {
        page,
        limit,
        collapseSeries,
        search: search || undefined,
        sort: sort || undefined,
        sortDesc: sortDesc || undefined,
      },
    })
  },

  getBookDetail(itemId: string) {
    return api.get<BookDetailResponse>(`/libraries/items/${itemId}`)
  },

  getSeries(libraryId: string, page = 0, limit = 20) {
    return api.get<AbsSeriesResponse>(`/libraries/library/${libraryId}/series`, {
      params: { page, limit },
    })
  },

  getSeriesDetail(seriesId: string, libraryId?: string) {
    return api.get<AbsSeriesItem>(`/libraries/series/${seriesId}`, {
      params: { libraryId: libraryId || undefined },
    })
  },

  getCoverUrl(itemId: string) {
    return `/api/libraries/items/${itemId}/cover`
  },
}

// --- Transfers ---

export const transferApi = {
  getTransfers(page = 0, limit = 20, status?: string) {
    return api.get<{ results: TransferResponse[]; total: number }>('/transfers', {
      params: { page, limit, status },
    })
  },

  getTransfer(transferId: string) {
    return api.get<TransferResponse>(`/transfers/detail/${transferId}`)
  },

  transferBook(request: {
    absLibraryItemId: string
    category?: string
    playbackType?: string
    overrideMinAge?: number
    overrideMaxAge?: number
  }) {
    return api.post('/transfers/book', request)
  },

  transferSeries(request: {
    absSeriesId: string
    absLibraryId: string
    category?: string
    oneCardPerBook?: boolean
    overrideMinAge?: number
    overrideMaxAge?: number
  }) {
    return api.post<{ jobId: string; message: string }>('/transfers/series', request)
  },

  // Phase 2: Batch transfer
  transferBatch(request: {
    absLibraryItemIds: string[]
    category?: string
    playbackType?: string
    overrideMinAge?: number
    overrideMaxAge?: number
  }) {
    return api.post<BatchTransferResponse>('/transfers/batch', request)
  },

  // Phase 6 (wired now): Retry + Cancel
  retryTransfer(transferId: string) {
    return api.post(`/transfers/retry/${transferId}`)
  },

  cancelTransfer(transferId: string) {
    return api.post(`/transfers/cancel/${transferId}`)
  },

  deleteTransfer(transferId: string) {
    return api.delete(`/transfers/${transferId}`)
  },

  clearCompleted() {
    return api.delete<{ cleared: number }>('/transfers/completed')
  },
}

// --- Cards (Phase 5) ---

export const cardsApi = {
  getCards() {
    return api.get<import('@/types').YotoCardSummary[]>('/cards')
  },

  getCard(cardId: string) {
    return api.get<import('@/types').YotoCardDetail>(`/cards/${cardId}`)
  },

  deleteCard(cardId: string) {
    return api.delete(`/cards/${cardId}`)
  },
}

// --- Series card sets ---

export interface CardSetPlanRequest {
  absSeriesId: string
  absLibraryId: string
  mode: PackingMode
  excludedItemIds?: string[]
  breaks?: number[] | null
  splitPoints?: Record<string, number[]> | null
}

export const cardSetApi = {
  plan(request: CardSetPlanRequest) {
    return api.post<CardSetPlanResponse>('/cardsets/plan', request)
  },

  create(request: CardSetPlanRequest & { name?: string; cardTitles?: string[] }) {
    return api.post<CardSetResponse>('/cardsets', request)
  },

  list(seriesId?: string) {
    return api.get<CardSetSummary[]>('/cardsets', { params: { seriesId: seriesId || undefined } })
  },

  /** newBooks: false skips reading Audiobookshelf, for frequent re-reads while sending. */
  get(cardSetId: string, newBooks = true) {
    return api.get<CardSetResponse>(`/cardsets/${cardSetId}`, {
      params: newBooks ? undefined : { newBooks: false },
    })
  },

  status(seriesIds: string[]) {
    return api.get<SeriesCardStatus[]>('/cardsets/status', {
      params: { seriesIds: seriesIds.join(',') },
    })
  },

  limits() {
    return api.get<CardLimits>('/cardsets/limits')
  },

  refresh(cardSetId: string) {
    return api.post<CardSetResponse>(`/cardsets/${cardSetId}/refresh`)
  },

  updateBreaks(cardSetId: string, breaks: number[], splitPoints: Record<string, number[]>) {
    return api.put<CardSetResponse>(`/cardsets/${cardSetId}/breaks`, { breaks, splitPoints })
  },

  update(
    cardSetId: string,
    changes: { name?: string; packingMode?: PackingMode; excludedItemIds?: string[] },
  ) {
    return api.patch<CardSetResponse>(`/cardsets/${cardSetId}`, changes)
  },

  renameCard(cardSetId: string, playlistId: string, name: string) {
    return api.patch<CardSetResponse>(`/cardsets/${cardSetId}/cards/${playlistId}`, { name })
  },

  remove(cardSetId: string) {
    return api.delete(`/cardsets/${cardSetId}`)
  },

  transfer(cardSetId: string, playlistIds?: string[]) {
    return api.post<{ cardSetId: string; jobId?: string; playlistIds?: string[] }>(
      `/cardsets/${cardSetId}/transfer`,
      { playlistIds: playlistIds ?? null },
    )
  },
}

// --- Admin analytics ---

export const adminApi = {
  getOverview() {
    return api.get<import('@/types').AdminOverview>('/admin/overview')
  },

  getUsers() {
    return api.get<import('@/types').AdminUser[]>('/admin/users')
  },

  getUsage(days = 14) {
    return api.get<import('@/types').UsagePoint[]>('/admin/usage', { params: { days } })
  },
}

export default api
