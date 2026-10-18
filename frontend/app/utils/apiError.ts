import type { ProblemDetails } from '../types/vacancy'

const NOT_REACHABLE = 'Das Backend ist nicht erreichbar.'
const GENERIC = 'Unbekannter Fehler.'

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function asProblem(value: unknown): ProblemDetails | null {
  if (!isRecord(value)) {
    return null
  }
  const title = typeof value.title === 'string' ? value.title : undefined
  const detail = typeof value.detail === 'string' ? value.detail : undefined
  const status = typeof value.status === 'number' ? value.status : undefined
  return title || detail ? { title, detail, status } : null
}

/**
 * Human-readable message for a failed API call ($fetch / useAsyncData error).
 * Uses the ProblemDetails body when there is one, otherwise the HTTP status.
 */
export function describeApiError(error: unknown): string {
  if (!isRecord(error)) {
    return GENERIC
  }

  const problem = asProblem(error.data)
  if (problem) {
    return [problem.title, problem.detail].filter(Boolean).join(' ')
  }

  const status = typeof error.statusCode === 'number' ? error.statusCode : undefined
  if (status === undefined || status === 0) {
    return NOT_REACHABLE
  }
  return `Fehler ${status}.`
}

/** HTTP status of a failed $fetch call, if any. */
export function errorStatus(error: unknown): number | undefined {
  return isRecord(error) && typeof error.statusCode === 'number' ? error.statusCode : undefined
}
