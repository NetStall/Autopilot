export interface Toast {
  id: number
  message: string
  kind: 'error' | 'info'
}

const DISMISS_AFTER_MS = 6000

/** Short messages at the bottom of the screen (e.g. „Status konnte nicht gespeichert werden"). */
export function useToasts() {
  const toasts = useState<Toast[]>('toasts', () => [])
  const nextId = useState('toast-next-id', () => 1)

  function dismiss(id: number) {
    toasts.value = toasts.value.filter(t => t.id !== id)
  }

  function show(message: string, kind: Toast['kind'] = 'info') {
    const id = nextId.value++
    toasts.value = [...toasts.value.slice(-3), { id, message, kind }]
    if (import.meta.client) {
      window.setTimeout(() => dismiss(id), DISMISS_AFTER_MS)
    }
  }

  return { toasts, show, error: (message: string) => show(message, 'error'), dismiss }
}
