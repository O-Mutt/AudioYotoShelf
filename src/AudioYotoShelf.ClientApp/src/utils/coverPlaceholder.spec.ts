import { describe, expect, it } from 'vitest'
import { COVER_PLACEHOLDER_SRC, onCoverError } from '@/utils/coverPlaceholder'

function errorOn(img: HTMLImageElement): Event {
  const event = new Event('error')
  Object.defineProperty(event, 'target', { value: img })
  return event
}

describe('onCoverError', () => {
  it('swaps a failed cover for the placeholder', () => {
    const img = document.createElement('img')
    img.src = '/api/libraries/items/item-1/cover'

    onCoverError(errorOn(img))

    expect(img.src).toBe(COVER_PLACEHOLDER_SRC)
  })

  it('leaves the placeholder alone if it fails too', () => {
    const img = document.createElement('img')
    img.src = COVER_PLACEHOLDER_SRC
    let writes = 0
    const descriptor = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src')!
    Object.defineProperty(img, 'src', {
      get: () => descriptor.get!.call(img),
      set: (value: string) => {
        writes++
        descriptor.set!.call(img, value)
      },
    })

    onCoverError(errorOn(img))

    expect(writes).toBe(0)
  })
})
