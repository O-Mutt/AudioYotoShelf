import { ref } from 'vue'

export type ToastVariant = 'success' | 'error' | 'info'

export interface Toast {
  id: string
  message: string
  variant: ToastVariant
  duration: number
}

// Singleton reactive state shared across all callers
const toasts = ref<Toast[]>([])
let counter = 0

function addToast(message: string, variant: ToastVariant, duration: number) {
  const id = `toast-${++counter}`
  const toast: Toast = { id, message, variant, duration }

  toasts.value = [...toasts.value, toast]

  // Cap at 5 visible
  if (toasts.value.length > 5) {
    toasts.value = toasts.value.slice(-5)
  }

  // Auto-dismiss
  setTimeout(() => {
    dismiss(id)
  }, duration)
}

function dismiss(id: string) {
  toasts.value = toasts.value.filter(t => t.id !== id)
}

/**
 * Phase 4: Toast notification composable.
 * SRP: Manages only the notification queue and auto-dismiss.
 * DI: Views call useToast().success() — never manage rendering.
 */
export function useToast() {
  return {
    toasts,
    success(message: string, duration = 5000) {
      addToast(message, 'success', duration)
    },
    error(message: string, duration = 8000) {
      addToast(message, 'error', duration)
    },
    info(message: string, duration = 5000) {
      addToast(message, 'info', duration)
    },
    dismiss,
  }
}
