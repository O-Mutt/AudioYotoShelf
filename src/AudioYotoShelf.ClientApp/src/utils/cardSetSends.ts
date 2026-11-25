import type { CardSetStatus, TransferStatus } from '@/types'

/** What to ask `GET /api/cardsets/sends` for; no status means every set. */
export interface CardSetSendsQuery {
  status?: CardSetStatus
}

// A Record so a new TransferStatus fails to compile until someone decides what it means for a set.
const SET_STATUS_FOR: Record<TransferStatus, CardSetStatus | null> = {
  Pending: 'Sending',
  DownloadingAudio: 'Sending',
  UploadingToYoto: 'Sending',
  AwaitingTranscode: 'Sending',
  GeneratingIcons: 'Sending',
  CreatingCard: 'Sending',
  Completed: 'Complete',
  Failed: 'NeedsAttention',
  // A set send has no cancelled state: cancelling puts its cards back to draft.
  Cancelled: null,
}

/**
 * The card sets the Transfers page's status filter stands for, or null when the filter matches no
 * set. The filter lists book-transfer statuses, so each maps onto the nearest set status.
 */
export function cardSetQueryFor(filter: TransferStatus | ''): CardSetSendsQuery | null {
  if (filter === '') return {}
  const status = SET_STATUS_FOR[filter]
  return status ? { status } : null
}

/** "just now", "5m ago", "3h ago" or "2d ago", counted back from `now` (ms). */
export function timeAgo(iso: string, now: number): string {
  const mins = Math.floor((now - new Date(iso).getTime()) / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}
