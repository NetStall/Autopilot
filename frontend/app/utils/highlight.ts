export interface TextSegment {
  text: string
  highlight: boolean
}

const WORD_CHAR = /[\p{L}\p{N}_]/u

function isWordChar(char: string | undefined): boolean {
  return char !== undefined && WORD_CHAR.test(char)
}

/** After a term, „#" and „+" also belong to the word: „C" is not in „C#", „C#" not in „C#x". */
function continuesWord(char: string | undefined): boolean {
  return isWordChar(char) || char === '#' || char === '+'
}

/**
 * Splits a title into plain and highlighted parts for the configured keywords (Ui:HighlightKeywords).
 * Case-insensitive and always whole words, like the backend's TermMatcher: no letter or digit right before the
 * match, none (and no „#"/„+") right after it — „Vue" not in „Revue", „ute" not in „heute", „.NET" not inside
 * „ASP.NET" (that is its own keyword), „C#" not in „C#x". Longer keywords win („ASP.NET" over „.NET").
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
      return !isWordChar(text[i - 1]) && !continuesWord(text[i + t.length])
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
