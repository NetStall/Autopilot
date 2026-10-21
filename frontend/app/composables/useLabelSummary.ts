import type { GoldLabelSummary } from '~/types/extraction'

/** „Bewertet: 12 / 30" — shared by the header and the label buttons, refreshed after every label. */
export function useLabelSummary() {
  const api = useVacanciesApi()
  const summary = useState<GoldLabelSummary | null>('gold-label-summary', () => null)

  async function refresh() {
    try {
      summary.value = await api.labelSummary()
    }
    catch {
      // The counter is a convenience; the page works without it.
    }
  }

  return { summary, refresh }
}
