export const COVER_PLACEHOLDER_SRC =
  'data:image/svg+xml,' +
  encodeURIComponent(`
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 300" fill="none">
      <rect width="200" height="300" rx="8" fill="#E5E7EB"/>
      <path d="M80 120h40v8H80v-8zm-8 24h56v6H72v-6zm0 16h56v6H72v-6zm0 16h40v6H72v-6z" fill="#9CA3AF"/>
      <rect x="76" y="80" width="48" height="24" rx="4" fill="#9CA3AF" opacity="0.5"/>
    </svg>
  `)

/** `@error` handler for a cover `<img>`: the cover proxy answers 404 for a book without a cover. */
export function onCoverError(e: Event): void {
  const img = e.target as HTMLImageElement
  // A placeholder that itself fails to load would fire error again, forever.
  if (img.src === COVER_PLACEHOLDER_SRC) return
  img.src = COVER_PLACEHOLDER_SRC
}
