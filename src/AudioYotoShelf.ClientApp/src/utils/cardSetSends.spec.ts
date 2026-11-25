import { describe, expect, it } from 'vitest'
import { cardSetQueryFor, timeAgo } from '@/utils/cardSetSends'
import type { TransferStatus } from '@/types'

describe('cardSetQueryFor', () => {
  it('asks for every set when there is no filter', () => {
    expect(cardSetQueryFor('')).toEqual({})
  })

  it('maps Completed to Complete and Failed to NeedsAttention', () => {
    expect(cardSetQueryFor('Completed')).toEqual({ status: 'Complete' })
    expect(cardSetQueryFor('Failed')).toEqual({ status: 'NeedsAttention' })
  })

  it.each<TransferStatus>([
    'Pending',
    'DownloadingAudio',
    'UploadingToYoto',
    'AwaitingTranscode',
    'GeneratingIcons',
    'CreatingCard',
  ])('maps the in-progress status %s to Sending', (status) => {
    expect(cardSetQueryFor(status)).toEqual({ status: 'Sending' })
  })

  it('shows no sets for Cancelled, which a set send never is', () => {
    expect(cardSetQueryFor('Cancelled')).toBeNull()
  })
})

describe('timeAgo', () => {
  const now = Date.parse('2026-11-25T12:00:00Z')

  it.each([
    ['2026-11-25T11:59:30Z', 'just now'],
    ['2026-11-25T11:55:00Z', '5m ago'],
    ['2026-11-25T09:00:00Z', '3h ago'],
    ['2026-11-23T11:00:00Z', '2d ago'],
  ])('%s is %s', (iso, expected) => {
    expect(timeAgo(iso, now)).toBe(expected)
  })
})
