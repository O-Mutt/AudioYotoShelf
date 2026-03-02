// --- Connection ---

export interface ConnectionStatus {
  id: string
  username: string
  absConnected: boolean
  audiobookshelfUrl: string
  yotoConnected: boolean
  yotoTokenExpiresAt: string | null
  defaultLibraryId: string | null
  defaultMinAge: number
  defaultMaxAge: number
}

export interface AbsConnectResponse {
  userConnectionId: string
  username: string
  absConnected: boolean
  yotoConnected: boolean
  defaultLibraryId: string | null
  libraries: string[] | null
}

export interface YotoDeviceCodeResponse {
  userCode: string
  verificationUri: string
  verificationUriComplete: string
  expiresIn: number
  interval: number
}

// --- Libraries ---

export interface AbsLibrary {
  id: string
  name: string
  mediaType: string
}

export interface AbsLibraryItemsResponse {
  results: AbsLibraryItem[]
  total: number
  limit: number
  page: number
}

export interface AbsLibraryItem {
  id: string
  ino: string
  libraryId: string
  mediaType: string
  media: AbsBookMedia | null
  numFiles: number
  size: number
}

export interface AbsBookMedia {
  metadata: AbsBookMetadata
  coverPath: string | null
  audioFiles: AbsAudioFile[]
  chapters: AbsChapter[]
  duration: number
  numTracks: number
  numAudioFiles: number
  numChapters: number
}

export interface AbsBookMetadata {
  title: string | null
  subtitle: string | null
  authors: { id: string; name: string }[]
  narrators: { id: string; name: string }[]
  series: { id: string; name: string; sequence: string | null }[]
  genres: string[]
  publishedYear: string | null
  publisher: string | null
  description: string | null
  language: string | null
  explicit: boolean
}

export interface AbsAudioFile {
  index: number
  ino: string
  metadata: { filename: string; ext: string; path: string; size: number }
  duration: number
  codec: string
  bitRate: number
  format: string
  size: number
}

export interface AbsChapter {
  id: number
  start: number
  end: number
  title: string
}

// --- Series ---

export interface AbsSeriesResponse {
  results: AbsSeriesItem[]
  total: number
  limit: number
  page: number
}

export interface AbsSeriesItem {
  id: string
  name: string
  description: string | null
  books: AbsSeriesBook[]
  totalDuration: number
}

export interface AbsSeriesBook {
  id: string
  media: AbsBookMedia | null
  sequence: string | null
}

// --- Transfers ---

export type TransferStatus =
  | 'Pending'
  | 'DownloadingAudio'
  | 'UploadingToYoto'
  | 'AwaitingTranscode'
  | 'GeneratingIcons'
  | 'CreatingCard'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'

export interface TransferResponse {
  id: string
  bookTitle: string
  bookAuthor: string | null
  seriesName: string | null
  seriesSequence: number | null
  status: TransferStatus
  progressPercent: number
  errorMessage: string | null
  ageRange: AgeRangeResponse
  yotoCardId: string | null
  createdAt: string
  completedAt: string | null
  tracks: TrackMappingResponse[]
}

export interface AgeRangeResponse {
  suggestedMin: number
  suggestedMax: number
  suggestionReason: string
  suggestionSource: string
  overrideMin: number | null
  overrideMax: number | null
  effectiveMin: number
  effectiveMax: number
}

export interface TrackMappingResponse {
  id: string
  chapterTitle: string
  chapterIndex: number
  duration: number
  isUploaded: boolean
  iconUrl: string | null
}

export interface TransferProgressUpdate {
  transferId: string
  status: TransferStatus
  progressPercent: number
  currentStep: string | null
  errorMessage: string | null
}

// --- Batch Transfer (Phase 2) ---

export interface BatchTransferResponse {
  batchId: string
  totalBooks: number
  queued: number
  jobIds: string[]
}

// --- Age Suggestion ---

export interface AgeSuggestionResponse {
  suggestedMinAge: number
  suggestedMaxAge: number
  reason: string
  source: string
  signals: { signal: string; value: string; weight: number }[]
}

// --- Book Detail (combined endpoint response) ---

export interface BookDetailResponse {
  item: AbsLibraryItem
  ageSuggestion: AgeSuggestionResponse | null
  existingTransfer: {
    id: string
    status: TransferStatus
    yotoCardId: string | null
    completedAt: string | null
  } | null
}
