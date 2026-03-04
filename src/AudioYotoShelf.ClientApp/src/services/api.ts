import axios from 'axios'
import type {
  AbsConnectResponse,
  AbsLibrary,
  AbsLibraryItemsResponse,
  AbsSeriesResponse,
  BatchTransferResponse,
  BookDetailResponse,
  ConnectionStatus,
  TransferResponse,
} from '@/types'

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

// --- Auth ---

export const authApi = {
  connectAbs(baseUrl: string, username: string, password: string) {
    return api.post<AbsConnectResponse>('/auth/abs/connect', { baseUrl, username, password })
  },

  validateAbsToken(userConnectionId: string) {
    return api.post<{ valid: boolean }>(`/auth/abs/validate/${userConnectionId}`)
  },

  getYotoAuthUrl(userConnectionId: string) {
    return api.get<{ authUrl: string }>(`/auth/yoto/authorize/${userConnectionId}`)
  },

  getConnectionStatus(userConnectionId: string) {
    return api.get<ConnectionStatus>(`/auth/status/${userConnectionId}`)
  },

  // Phase 3: Settings save
  updateSettings(userConnectionId: string, settings: {
    defaultLibraryId?: string
    defaultMinAge?: number
    defaultMaxAge?: number
  }) {
    return api.patch<ConnectionStatus>(`/auth/settings/${userConnectionId}`, settings)
  },
}

// --- Libraries ---

export const libraryApi = {
  getLibraries(userConnectionId: string) {
    return api.get<AbsLibrary[]>(`/libraries/${userConnectionId}`)
  },

  getLibraryItems(
    userConnectionId: string,
    libraryId: string,
    page = 0,
    limit = 20,
    collapseSeries = false,
    search?: string,
    sort?: string,
    sortDesc = false,
  ) {
    return api.get<AbsLibraryItemsResponse>(
      `/libraries/${userConnectionId}/library/${libraryId}/items`,
      { params: { page, limit, collapseSeries, search: search || undefined, sort: sort || undefined, sortDesc: sortDesc || undefined } }
    )
  },

  getBookDetail(userConnectionId: string, itemId: string) {
    return api.get<BookDetailResponse>(`/libraries/${userConnectionId}/items/${itemId}`)
  },

  getSeries(userConnectionId: string, libraryId: string, page = 0, limit = 20) {
    return api.get<AbsSeriesResponse>(
      `/libraries/${userConnectionId}/library/${libraryId}/series`,
      { params: { page, limit } }
    )
  },

  getSeriesDetail(userConnectionId: string, seriesId: string) {
    return api.get(`/libraries/${userConnectionId}/series/${seriesId}`)
  },

  getCoverUrl(userConnectionId: string, itemId: string) {
    return `/api/libraries/${userConnectionId}/items/${itemId}/cover`
  },
}

// --- Transfers ---

export const transferApi = {
  getTransfers(userConnectionId: string, page = 0, limit = 20, status?: string) {
    return api.get<{ results: TransferResponse[]; total: number }>(
      `/transfers/${userConnectionId}`,
      { params: { page, limit, status } }
    )
  },

  getTransfer(transferId: string) {
    return api.get<TransferResponse>(`/transfers/detail/${transferId}`)
  },

  transferBook(userConnectionId: string, request: {
    absLibraryItemId: string
    category?: string
    playbackType?: string
    overrideMinAge?: number
    overrideMaxAge?: number
  }) {
    return api.post(`/transfers/${userConnectionId}/book`, request)
  },

  transferSeries(userConnectionId: string, request: {
    absSeriesId: string
    absLibraryId: string
    category?: string
    oneCardPerBook?: boolean
    overrideMinAge?: number
    overrideMaxAge?: number
  }) {
    return api.post(`/transfers/${userConnectionId}/series`, request)
  },

  // Phase 2: Batch transfer
  transferBatch(userConnectionId: string, request: {
    absLibraryItemIds: string[]
    category?: string
    playbackType?: string
    overrideMinAge?: number
    overrideMaxAge?: number
  }) {
    return api.post<BatchTransferResponse>(`/transfers/${userConnectionId}/batch`, request)
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
}

// --- Cards (Phase 5) ---

export const cardsApi = {
  getCards(userConnectionId: string) {
    return api.get<import('@/types').YotoCardSummary[]>(`/cards/${userConnectionId}`)
  },

  getCard(userConnectionId: string, cardId: string) {
    return api.get<import('@/types').YotoCardDetail>(`/cards/${userConnectionId}/${cardId}`)
  },

  deleteCard(userConnectionId: string, cardId: string) {
    return api.delete(`/cards/${userConnectionId}/${cardId}`)
  },
}

export default api
