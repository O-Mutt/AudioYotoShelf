import type { AbsSeriesItem, CardLimits, SeriesCardStatus } from '@/types'

export type SeriesCardFilter = 'all' | 'not-on-cards' | 'new-books'

export interface SeriesCardBadge {
  label: string
  tone: 'green' | 'blue' | 'amber' | 'red' | 'gray'
}

/** Books of the series that are on none of its set's cards and not left out. */
export function newBookCount(series: AbsSeriesItem, status: SeriesCardStatus | undefined): number {
  if (!status) return 0
  const known = new Set(status.knownBookIds)
  return series.books.filter((b) => !known.has(b.id)).length
}

/**
 * The series' size at the estimated stored bitrate fits one card. Size only: a long series of short
 * books can still need a second card for the track limit, which the plan on the series page shows.
 */
export function fitsOneCard(series: AbsSeriesItem, limits: CardLimits | null): boolean {
  if (!limits) return false
  const bytes = (series.totalDuration * limits.transcodedBitrateKbps * 1000) / 8
  const withinDuration =
    limits.maxTotalDurationSeconds == null || series.totalDuration <= limits.maxTotalDurationSeconds
  return bytes <= limits.maxTotalBytes && withinDuration
}

/** The badges a series tile shows (spec §8): card state first, then new books. */
export function seriesCardBadges(
  series: AbsSeriesItem,
  status: SeriesCardStatus | undefined,
  limits: CardLimits | null,
): SeriesCardBadge[] {
  if (!status) {
    return [
      fitsOneCard(series, limits)
        ? { label: 'Fits on 1 card', tone: 'gray' }
        : { label: 'Not on cards', tone: 'gray' },
    ]
  }

  const cards = (n: number) => `${n} ${n === 1 ? 'card' : 'cards'}`
  const badges: SeriesCardBadge[] = []
  if (status.sending) {
    badges.push({
      label: `Sending card ${Math.min(status.sentCount + 1, status.cardCount)} of ${status.cardCount}`,
      tone: 'blue',
    })
  } else if (status.status === 'NeedsAttention') {
    badges.push({ label: 'A card failed', tone: 'red' })
  } else if (status.sentCount === status.cardCount) {
    badges.push({ label: `On ${cards(status.cardCount)}`, tone: 'green' })
  } else if (status.sentCount > 0) {
    badges.push({ label: `On ${status.sentCount} of ${cards(status.cardCount)}`, tone: 'blue' })
  } else {
    badges.push({ label: `${cards(status.cardCount)} planned`, tone: 'gray' })
  }

  const fresh = newBookCount(series, status)
  if (fresh > 0)
    badges.push({ label: `${fresh} new ${fresh === 1 ? 'book' : 'books'}`, tone: 'amber' })
  return badges
}

export function matchesFilter(
  filter: SeriesCardFilter,
  series: AbsSeriesItem,
  status: SeriesCardStatus | undefined,
): boolean {
  if (filter === 'not-on-cards') return !status
  if (filter === 'new-books') return newBookCount(series, status) > 0
  return true
}
