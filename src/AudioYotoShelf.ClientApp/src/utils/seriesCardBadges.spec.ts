import { describe, expect, it } from 'vitest'
import { matchesFilter, seriesCardBadges } from '@/utils/seriesCardBadges'
import type { AbsSeriesItem, CardLimits, SeriesCardStatus } from '@/types'

const limits: CardLimits = {
  maxTracks: 100,
  maxTotalBytes: 524_288_000,
  maxTotalDurationSeconds: null,
  transcodedBitrateKbps: 100,
}

function series(bookIds: string[], hours = 3): AbsSeriesItem {
  return {
    id: 'ser-1',
    name: 'Dragons',
    description: null,
    books: bookIds.map((id) => ({ id, media: null, sequence: null })),
    totalDuration: hours * 3600,
  }
}

function status(extra: Partial<SeriesCardStatus> = {}): SeriesCardStatus {
  return {
    absSeriesId: 'ser-1',
    cardSetId: 'set-1',
    cardCount: 3,
    sentCount: 0,
    sending: false,
    status: 'Draft',
    knownBookIds: ['b1', 'b2'],
    ...extra,
  }
}

const labels = (s: AbsSeriesItem, st?: SeriesCardStatus) =>
  seriesCardBadges(s, st, limits).map((b) => b.label)

describe('seriesCardBadges', () => {
  it('says whether a series without cards fits on one', () => {
    expect(labels(series(['b1'], 3))).toEqual(['Fits on 1 card'])
    expect(labels(series(['b1'], 20))).toEqual(['Not on cards'])
  })

  it('counts the cards on Yoto', () => {
    expect(labels(series(['b1', 'b2']), status({ sentCount: 3, status: 'Complete' }))).toEqual([
      'On 3 cards',
    ])
    expect(labels(series(['b1', 'b2']), status({ sentCount: 1 }))).toEqual(['On 1 of 3 cards'])
    expect(labels(series(['b1', 'b2']), status())).toEqual(['3 cards planned'])
  })

  it('shows sending and failures first', () => {
    expect(labels(series(['b1', 'b2']), status({ sentCount: 1, sending: true }))).toEqual([
      'Sending card 2 of 3',
    ])
    expect(labels(series(['b1', 'b2']), status({ status: 'NeedsAttention' }))).toEqual([
      'A card failed',
    ])
  })

  it('adds the books new since the cards were made', () => {
    expect(
      labels(series(['b1', 'b2', 'b3']), status({ sentCount: 3, status: 'Complete' })),
    ).toEqual(['On 3 cards', '1 new book'])
  })
})

describe('matchesFilter', () => {
  it('filters on cards and on new books', () => {
    expect(matchesFilter('not-on-cards', series(['b1']), undefined)).toBe(true)
    expect(matchesFilter('not-on-cards', series(['b1']), status())).toBe(false)
    expect(matchesFilter('new-books', series(['b1', 'b2', 'b9']), status())).toBe(true)
    expect(matchesFilter('new-books', series(['b1', 'b2']), status())).toBe(false)
    expect(matchesFilter('all', series(['b1']), undefined)).toBe(true)
  })
})
