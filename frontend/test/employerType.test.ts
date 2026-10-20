import { describe, expect, it } from 'vitest'
import { employerTypeLabel, isIntermediary, sourceAttribution, sourceLabel } from '../app/utils/employerType'

describe('employerTypeLabel', () => {
  it('returns German labels for every employer type', () => {
    expect(employerTypeLabel('Direct')).toBe('Direkter Arbeitgeber')
    expect(employerTypeLabel('Agency')).toBe('Personalvermittlung')
    expect(employerTypeLabel('Temporary')).toBe('Zeitarbeit')
    expect(employerTypeLabel('Unknown')).toBe('Unbekannt')
  })
})

describe('isIntermediary', () => {
  it('marks agencies and temporary work, not direct employers', () => {
    expect(isIntermediary('Agency')).toBe(true)
    expect(isIntermediary('Temporary')).toBe(true)
    expect(isIntermediary('Direct')).toBe(false)
    expect(isIntermediary('Unknown')).toBe(false)
  })
})

describe('sourceLabel', () => {
  it('returns German labels for every source', () => {
    expect(sourceLabel('Arbeitsagentur')).toBe('Arbeitsagentur')
    expect(sourceLabel('Manual')).toBe('Manuell erfasst')
  })
})

describe('sourceAttribution', () => {
  it('credits Himalayas with a visible link to its site (API terms)', () => {
    expect(sourceAttribution('Himalayas')).toEqual({ label: 'via Himalayas', href: 'https://himalayas.app' })
    expect(sourceLabel('Himalayas')).toBe('Himalayas')
  })

  it('has no attribution for manual vacancies', () => {
    expect(sourceAttribution('Manual')).toBeNull()
  })
})
