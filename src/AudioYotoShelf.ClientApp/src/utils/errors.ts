import { isAxiosError } from 'axios'

/** Extract a human-readable message from an unknown caught error (axios or otherwise). */
export function errorMessage(err: unknown, fallback = 'Something went wrong'): string {
  if (isAxiosError(err)) return err.response?.data?.message ?? err.message
  if (err instanceof Error) return err.message
  return fallback
}

/** HTTP status code from an axios error, or undefined for non-HTTP failures. */
export function statusCode(err: unknown): number | undefined {
  return isAxiosError(err) ? err.response?.status : undefined
}
