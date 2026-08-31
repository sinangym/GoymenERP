/**
 * TEMPORARY contract fixtures for SHELL-PILOT-01B parallel development.
 *
 * Issue #129 "Parallel-development rule": before the generated TypeScript client
 * (Issue #122) lands, Claude may build shell structure against fixtures that
 * mirror the parent contract — but must NOT hand-write a second permanent DTO
 * source.
 *
 * These shapes MIRROR the merged SHELL-PILOT-01A backend (PR #132, main d3f2b30):
 *   GET /api/v1/shell/capabilities   -> ShellCapabilitySnapshot | 503 (fail-closed)
 *   GET /api/v1/organization/companies -> CompanySummary[]
 *
 * >>> DELETION CONTRACT <<<
 * Every symbol here is removed when the generated client lands. The shell store
 * imports context/capability *shapes* only from here; final integration
 * re-points them at `src/api/generated/**` and a drift check enforces the
 * boundary. No business truth is asserted: no real company names/VKN, no KPIs,
 * balances, permissions or routes.
 */

import type { CompanyContext } from './shellState'

/** Mirrors server `CompanySummary(id, code, legalName, isPlaceholder)`. */
export interface CompanyDirectoryEntryFixture {
  readonly id: string
  readonly code: string
  readonly legalName: string
  readonly isPlaceholder: boolean
}

/**
 * Mirrors server `ShellCapabilitySnapshot(contractVersion, generation, capabilities)`.
 * `generation` is an opaque protected token — compared for equality only, never parsed.
 * `capabilities` is the list of allowed capability ids (ImplementationAvailable && allowed).
 */
export interface CapabilitySnapshotFixture {
  readonly contractVersion: string
  readonly generation: string
  readonly capabilities: readonly string[]
}

export const companyDirectoryFixture: readonly CompanyDirectoryEntryFixture[] = [
  { id: 'fixture-company-a', code: 'GOY-A', legalName: 'TBD-GOY-01', isPlaceholder: true },
  { id: 'fixture-company-b', code: 'GOY-B', legalName: 'TBD-GOY-02', isPlaceholder: true },
]

export const capabilitySnapshotFixture: CapabilitySnapshotFixture = {
  contractVersion: 'fixture-shell-capability-v1',
  generation: 'fixture-cap-gen-1',
  // SHELL-PILOT scope: no business capability is mounted yet.
  capabilities: [],
}

/** The backend answers 503 when the snapshot fails closed (ShellCapabilityUnavailableException). */
export const capabilityUnavailableStatus = 503 as const

export function fixtureContext(
  companyId: string,
  generation: string | null = capabilitySnapshotFixture.generation,
): CompanyContext {
  return {
    userId: 'fixture-user',
    scopeMode: 'single',
    companyId,
    capabilityGeneration: generation,
  }
}
