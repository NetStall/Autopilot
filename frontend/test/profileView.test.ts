import { describe, expect, it } from 'vitest'
import type { ProfileSkill } from '../app/types/profile'
import { formatPeriod, skillLevelLabel, skillsByLevel } from '../app/utils/profileView'

const skill = (name: string, level: ProfileSkill['level']): ProfileSkill =>
  ({ name, level, aliases: [], years: null, group: null })

describe('profile view', () => {
  it('groups skills by level in level order, sorted by name, without empty groups', () => {
    const groups = skillsByLevel([skill('Vue', 'Production'), skill('React', 'None'), skill('C#', 'Production')])

    expect(groups.map(g => g.level)).toEqual(['Production', 'None'])
    expect(groups[0]!.skills.map(s => s.name)).toEqual(['C#', 'Vue'])
  })

  it('labels levels in German', () => {
    expect(skillLevelLabel('Production')).toBe('Beruflich')
    expect(skillLevelLabel('None')).toBe('Keine Erfahrung')
  })

  it('formats periods', () => {
    expect(formatPeriod('2022-07', '2025-09')).toBe('07/2022 – 09/2025')
    expect(formatPeriod('2025-12', null)).toBe('12/2025 – heute')
  })
})
