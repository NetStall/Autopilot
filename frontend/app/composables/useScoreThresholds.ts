import type { ScoreThresholds } from '~/utils/vacancyBadges'

/** Colour thresholds of the score badge, loaded once from GET /api/settings/ui (Ui:ScoreHigh / Ui:ScoreMedium). */
export function useScoreThresholds() {
  const thresholds = useState<ScoreThresholds>('score-thresholds', () => ({ ...DEFAULT_SCORE_THRESHOLDS }))
  const loaded = useState('score-thresholds-loaded', () => false)
  if (import.meta.client && !loaded.value) {
    loaded.value = true
    void useVacanciesApi().uiSettings()
      .then((settings) => {
        thresholds.value = { high: settings.scoreHigh ?? DEFAULT_SCORE_THRESHOLDS.high, medium: settings.scoreMedium ?? DEFAULT_SCORE_THRESHOLDS.medium }
      })
      .catch(() => {
        // Defaults stay — the colour is only a cue, the number is always shown.
      })
  }
  return thresholds
}
