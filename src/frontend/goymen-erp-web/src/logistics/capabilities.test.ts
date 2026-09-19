/**
 * The Logistics lane's mount ids, routes and screen ids are pinned to the backend provider's TEXT
 * (`LogisticsShellCapabilityProvider.cs`, LOG-4a #350). A backend rename turns this red instead of
 * leaving a screen silently unmounted. Read with Node's own `fs` (the test runner's process), as the
 * Manufacturing OpenAPI pin does: the file lives outside the web package.
 *
 * WHAT THIS IS NOT: availability. The provider declares the three STAGED; this test pins that too,
 * so the flip (a separate exact-head change) is visible here.
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { LOGISTICS_CAPABILITY, LOGISTICS_NAV_LABEL, LOGISTICS_ROUTE, LOGISTICS_SCREEN_ID, instructionDetailRoute } from './capabilities'

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
// Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
const providerText = fs.readFileSync(
  path.resolve(
    nodeProcess.cwd(),
    '../../backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Shell/LogisticsShellCapabilityProvider.cs',
  ),
  'utf8',
)

const constant = (name: string): string => {
  const match = new RegExp(`public const string ${name} = "([^"]+)";`).exec(providerText)
  if (match === null) throw new Error(`the provider declares no ${name}`)
  return match[1]
}

describe('Logistics lane ↔ backend shell provider', () => {
  it('declares the same capability ids the provider does', () => {
    expect(constant('ListCapabilityId')).toBe(LOGISTICS_CAPABILITY.instructionsList)
    expect(constant('CreateCapabilityId')).toBe(LOGISTICS_CAPABILITY.instructionsCreate)
    expect(constant('DetailCapabilityId')).toBe(LOGISTICS_CAPABILITY.instructionsDetail)
  })

  it('mounts the same routes, and the same master v3 §27.8 screen ids', () => {
    expect(constant('ListRoutePath')).toBe(LOGISTICS_ROUTE.list)
    expect(constant('CreateRoutePath')).toBe(LOGISTICS_ROUTE.create)
    expect(constant('DetailRoutePath')).toBe(LOGISTICS_ROUTE.detail)
    expect(constant('ListScreenId')).toBe(LOGISTICS_SCREEN_ID[LOGISTICS_CAPABILITY.instructionsList])
    expect(constant('CreateScreenId')).toBe(LOGISTICS_SCREEN_ID[LOGISTICS_CAPABILITY.instructionsCreate])
    expect(constant('DetailScreenId')).toBe(LOGISTICS_SCREEN_ID[LOGISTICS_CAPABILITY.instructionsDetail])
  })

  it('the provider keeps every declaration STAGED until the flip', () => {
    expect((providerText.match(/ImplementationAvailable: false/g) ?? []).length).toBe(3)
    expect(providerText).not.toMatch(/ImplementationAvailable: true/)
  })

  it('the detail link helper matches the mounted pattern and the detail has no nav entry', () => {
    const id = '7b1c0f2e-0000-4a00-8000-000000000002'
    expect(matchPath(LOGISTICS_ROUTE.detail, instructionDetailRoute(id))?.params.instructionId).toBe(id)
    expect(matchPath(LOGISTICS_ROUTE.detail, LOGISTICS_ROUTE.create)?.params.instructionId).toBe('yeni')
    expect(LOGISTICS_NAV_LABEL[LOGISTICS_CAPABILITY.instructionsDetail]).toBe('')
  })
})
