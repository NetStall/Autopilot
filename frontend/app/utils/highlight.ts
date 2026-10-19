export interface TextSegment {
  text: string
  highlight: boolean
}

const WORD_CHAR = /[\p{L}\p{N}]/u

function isWordChar(char: string | undefined): boolean {
  return char !== undefined && WORD_CHAR.test(char)
}

/**
 * Splits a title into plain and highlighted parts for the configured keywords (Ui:HighlightKeywords).
 * Case-insensitive; a keyword only counts as a whole token where it starts/ends with a letter or digit
 * („Vue" not in „Revue"; „.NET" also inside „ASP.NET"). Longer keywords win („ASP.NET" over „.NET").
 * Rendered with <mark> per segment — never as HTML.
 */
export function highlightSegments(text: string, keywords: readonly string[]): TextSegment[] {
  const terms = [...new Set(keywords.map(k => k.trim()).filter(k => k.length > 0))]
    .sort((a, b) => b.length - a.length)
  if (terms.length === 0 || text.length === 0) {
    return text ? [{ text, highlight: false }] : []
  }

  const lower = text.toLocaleLowerCase('de-DE')
  const segments: TextSegment[] = []
  let plainStart = 0
  let i = 0

  while (i < text.length) {
    const term = terms.find((t) => {
      if (!lower.startsWith(t.toLocaleLowerCase('de-DE'), i)) {
        return false
      }
      const startsWithWordChar = isWordChar(t[0])
      const endsWithWordChar = isWordChar(t[t.length - 1])
      return (!startsWithWordChar || !isWordChar(text[i - 1]))
        && (!endsWithWordChar || !isWordChar(text[i + t.length]))
    })

    if (term) {
      if (i > plainStart) {
        segments.push({ text: text.slice(plainStart, i), highlight: false })
      }
      segments.push({ text: text.slice(i, i + term.length), highlight: true })
      i += term.length
      plainStart = i
    }
    else {
      i++
    }
  }

  if (plainStart < text.length) {
    segments.push({ text: text.slice(plainStart), highlight: false })
  }
  return segments
}
