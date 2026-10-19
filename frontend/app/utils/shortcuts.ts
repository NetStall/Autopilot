export type ListShortcut = 'next' | 'previous' | 'open' | 'toggleSeen' | 'toggleStar' | 'toggleDismiss' | 'help' | 'close'

export const SHORTCUT_HELP: readonly { keys: string, action: string }[] = [
  { keys: 'j', action: 'Nächste Stelle' },
  { keys: 'k', action: 'Vorherige Stelle' },
  { keys: 'o', action: 'Originalanzeige öffnen (markiert als gesehen)' },
  { keys: 's', action: 'Gesehen / neu umschalten' },
  { keys: 'm', action: 'Merken / nicht mehr merken' },
  { keys: 'x', action: 'Ausblenden / wieder einblenden' },
  { keys: '?', action: 'Diese Hilfe' },
  { keys: 'Esc', action: 'Hilfe schließen' },
]

const KEYS: Record<string, ListShortcut> = {
  j: 'next',
  k: 'previous',
  o: 'open',
  s: 'toggleSeen',
  m: 'toggleStar',
  x: 'toggleDismiss',
  '?': 'help',
  Escape: 'close',
}

export interface KeyEventLike {
  key: string
  ctrlKey?: boolean
  metaKey?: boolean
  altKey?: boolean
  isComposing?: boolean
  target?: EventTarget | null
}

interface ElementLike {
  tagName?: string
  isContentEditable?: boolean
}

/** Shortcuts must not fire while the user types into a field. */
export function isTypingTarget(target: EventTarget | null | undefined): boolean {
  const element = target as ElementLike | null | undefined
  if (!element) {
    return false
  }
  const tag = element.tagName?.toUpperCase()
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || element.isContentEditable === true
}

/** Maps a keydown to a list action, or null when it is not a shortcut (or must be ignored). */
export function shortcutFor(event: KeyEventLike): ListShortcut | null {
  if (event.ctrlKey || event.metaKey || event.altKey || event.isComposing) {
    return null
  }
  if (event.key !== 'Escape' && isTypingTarget(event.target)) {
    return null
  }
  return KEYS[event.key] ?? null
}

/** Index of the next/previous card, clamped to the list. -1 = nothing active yet. */
export function moveActiveIndex(current: number, count: number, direction: 'next' | 'previous'): number {
  if (count === 0) {
    return -1
  }
  if (current < 0 || current >= count) {
    return direction === 'next' ? 0 : count - 1
  }
  return direction === 'next' ? Math.min(current + 1, count - 1) : Math.max(current - 1, 0)
}
