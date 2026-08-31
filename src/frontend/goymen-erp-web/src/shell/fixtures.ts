/**
 * Test-only helper for the reducer + fence unit tests. A verified CompanyContext
 * with sensible defaults. Not used by production code.
 */

import type { CompanyContext } from './shellState'

export function fixtureContext(
  companyId: string,
  capabilityGeneration: string | null = 'fixture-cap-gen-1',
): CompanyContext {
  return { userId: 'fixture-user', scopeMode: 'single', companyId, capabilityGeneration }
}
