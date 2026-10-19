import { describe, expect, it } from 'vitest'
import { highlightSegments } from '../app/utils/highlight'

const KEYWORDS = ['C#', '.NET', 'ASP.NET', 'Vue', 'TypeScript', 'Full Stack', 'Fullstack', 'Backend', 'Remote']
const marked = (title: string, keywords = KEYWORDS) =>
  highlightSegments(title, keywords).filter(s => s.highlight).map(s => s.text)

describe('highlightSegments', () => {
  it('marks configured keywords case-insensitively and keeps the original spelling', () => {
    expect(marked('Senior .net Entwickler (m/w/d) – C#, vue.js, 100 % REMOTE'))
      .toEqual(['.net', 'C#', 'vue', 'REMOTE'])
  })

  it('splits the title into plain and marked parts that add up to the title', () => {
    const title = 'Full Stack Developer C# / Vue'
    const segments = highlightSegments(title, KEYWORDS)

    expect(segments.map(s => s.text).join('')).toBe(title)
    expect(segments).toEqual([
      { text: 'Full Stack', highlight: true },
      { text: ' Developer ', highlight: false },
      { text: 'C#', highlight: true },
      { text: ' / ', highlight: false },
      { text: 'Vue', highlight: true },
    ])
  })

  it('prefers the longest keyword', () => {
    expect(marked('Backend Developer ASP.NET Core')).toEqual(['Backend', 'ASP.NET'])
  })

  it('does not mark keywords inside other words', () => {
    expect(marked('Revue-Redakteur, Backendtechnik, Fullstackers')).toEqual([])
  })

  it('returns the whole text unmarked without keywords', () => {
    expect(highlightSegments('Softwareentwickler', [])).toEqual([{ text: 'Softwareentwickler', highlight: false }])
    expect(highlightSegments('', KEYWORDS)).toEqual([])
  })

  it('treats markup in titles as plain text', () => {
    expect(highlightSegments('<b>Vue</b>', KEYWORDS).map(s => s.text)).toEqual(['<b>', 'Vue', '</b>'])
  })
})
