import type { ProfileSkill, SkillLevel } from '../types/profile'

export const SKILL_LEVELS: readonly SkillLevel[] = ['Production', 'Project', 'Learning', 'None']

const levelLabels: Record<SkillLevel, string> = {
  Production: 'Beruflich',
  Project: 'Eigene Projekte',
  Learning: 'Lerne ich gerade',
  None: 'Keine Erfahrung',
}

/** German label of a skill level. */
export function skillLevelLabel(level: SkillLevel): string {
  return levelLabels[level]
}

/** Skills grouped by level, in level order, each group sorted by name; empty groups are left out. */
export function skillsByLevel(skills: readonly ProfileSkill[]): { level: SkillLevel, skills: ProfileSkill[] }[] {
  return SKILL_LEVELS
    .map(level => ({
      level,
      skills: skills.filter(s => s.level === level).sort((a, b) => a.name.localeCompare(b.name, 'de')),
    }))
    .filter(group => group.skills.length > 0)
}

/** „07/2022 – 09/2025" or „07/2022 – heute" from YYYY-MM values. */
export function formatPeriod(from: string, to: string | null): string {
  const month = (value: string) => {
    const [year, m] = value.split('-')
    return year && m ? `${m}/${year}` : value
  }
  return `${month(from)} – ${to ? month(to) : 'heute'}`
}
