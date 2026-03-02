import axios from 'axios'
import type {
  AbsConnectResponse,
  AbsLibrary,
  AbsLibraryItemsResponse,
  AbsSeriesResponse,
  BookDetailResponse,
  ConnectionStatus,
  TransferResponse,
  YotoDeviceCodeResponse,
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

  initiateYotoAuth(userConnectionId: string) {
    return api.post<YotoDeviceCodeResponse>(`/auth/yoto/initiate/${userConnectionId}`)
  },

  pollYotoAuth(userConnectionId: string) {
    return api.post<{ status: string; yotoConnected?: boolean }>(`/auth/yoto/poll/${userConnectionId}`)
  },

  getConnectionStatus(userConnectionId: string) {
    return api.get<ConnectionStatus>(`/auth/status/${userConnectionId}`)
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
  ) {
    return api.get<AbsLibraryItemsResponse>(
      `/libraries/${userConnectionId}/library/${libraryId}/items`,
      { params: { page, limit, collapseSeries, search: search || undefined, sort: sort || undefined } }
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
}

export default api
