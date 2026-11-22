import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import { WORK_CAPABILITY, WORK_MY_TASKS_CAPABILITIES } from './capabilities'
import { MY_TASKS_ROUTE } from './collaborationContract'
import { workRoutes } from './workRoutes'

describe('WP9 Work shell capabilities', () => {
  it('mirrors WorkShellCapabilityProvider 1.0 ids exactly (route + control) and gates the "Bana atanan" route by the route id', () => {
    expect(WORK_CAPABILITY).toEqual({ myTasks: 'work.tasks.mine', assignTask: 'work.tasks.assign' })
    expect(WORK_MY_TASKS_CAPABILITIES).toEqual(['work.tasks.mine'])
    expect(MY_TASKS_ROUTE).toBe('/gorevlerim')
    expect(matchRoutes(workRoutes, MY_TASKS_ROUTE)?.at(-1)?.route.path).toBe('gorevlerim')
  })
})
