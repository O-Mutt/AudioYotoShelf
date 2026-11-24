import { isAxiosError } from 'axios'

/** Extract a human-readable message from an unknown caught error (axios or otherwise). */
export function errorMessage(err: unknown, fallback = 'Something went wrong'): string {
  if (isAxiosError(err)) return err.response?.data?.message ?? err.message
  if (err instanceof Error) return err.message
  return fallback
}

/**
 * The server's message when it refused a book for being over the Yoto card limits: a 409 whose body
 * carries `capacity`. Null for any other error, including other 409s.
 */
export function capacityRefusalMessage(err: unknown): string | null {
  if (!isAxiosError(err) || err.response?.status !== 409 || !err.response.data?.capacity)
    return null
  return errorMessage(err)
}

/** HTTP status code from an axios error, or undefined for non-HTTP failures. */
export function statusCode(err: unknown): number | undefined {
  return isAxiosError(err) ? err.response?.status : undefined
}
