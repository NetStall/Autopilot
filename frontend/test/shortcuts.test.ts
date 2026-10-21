import { describe, expect, it } from 'vitest'
import { isTypingTarget, moveActiveIndex, shortcutFor } from '../app/utils/shortcuts'

const el = (tagName: string, isContentEditable = false) => ({ tagName, isContentEditable }) as unknown as EventTarget

describe('shortcutFor', () => {
  it.each([
    ['j', 'next'],
    ['k', 'previous'],
    ['o', 'open'],
    ['s', 'toggleSeen'],
    ['m', 'toggleStar'],
    ['x', 'toggleDismiss'],
    ['p', 'labelFits'],
    ['n', 'labelNotFits'],
    ['?', 'help'],
    ['Escape', 'close'],
  ])('maps %s to %s', (key, action) => {
    expect(shortcutFor({ key, target: el('BODY') })).toBe(action)
  })

  it.each(['INPUT', 'TEXTAREA', 'SELECT', 'input'])('is ignored while typing in %s', (tag) => {
    expect(shortcutFor({ key: 'j', target: el(tag) })).toBeNull()
    expect(shortcutFor({ key: 's', target: el(tag) })).toBeNull()
  })

  it('is ignored in contenteditable elements', () => {
    expect(shortcutFor({ key: 'm', target: el('DIV', true) })).toBeNull()
  })

  it('still closes the help with Escape from an input', () => {
    expect(shortcutFor({ key: 'Escape', target: el('INPUT') })).toBe('close')
  })

  it('ignores combinations with modifier keys and IME composition', () => {
    expect(shortcutFor({ key: 's', ctrlKey: true })).toBeNull()
    expect(shortcutFor({ key: 'k', metaKey: true })).toBeNull()
    expect(shortcutFor({ key: 'j', altKey: true })).toBeNull()
    expect(shortcutFor({ key: 'j', isComposing: true })).toBeNull()
  })

  it('ignores other keys', () => {
    expect(shortcutFor({ key: 'J' })).toBeNull()
    expect(shortcutFor({ key: 'Enter' })).toBeNull()
    expect(isTypingTarget(null)).toBe(false)
  })
})

describe('moveActiveIndex', () => {
  it('starts at the first card for j and at the last for k', () => {
    expect(moveActiveIndex(-1, 5, 'next')).toBe(0)
    expect(moveActiveIndex(-1, 5, 'previous')).toBe(4)
  })

  it('moves and stops at the ends', () => {
    expect(moveActiveIndex(1, 5, 'next')).toBe(2)
    expect(moveActiveIndex(4, 5, 'next')).toBe(4)
    expect(moveActiveIndex(0, 5, 'previous')).toBe(0)
  })

  it('handles an empty list and a stale index', () => {
    expect(moveActiveIndex(0, 0, 'next')).toBe(-1)
    expect(moveActiveIndex(9, 3, 'next')).toBe(0)
  })
})
