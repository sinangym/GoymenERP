/**
 * Emits `c01-e2e-evidence.json` and `c01-e2e-network-summary.json`.
 *
 * WHAT THIS FILE DELIBERATELY DOES NOT DO: assert the fixture's identity from
 * constants of its own. The validator checks the fixture SHA-256, byte length
 * and row counts, so a reporter that hardcoded them would emit evidence that
 * agrees with the validator by construction and says nothing about the database
 * the browser actually read. Those values are therefore READ from
 * `c01-e2e-fixture-evidence.json`, which the materializer wrote after querying
 * the live SQL Server. If the stack drifts, the two disagree and the run fails —
 * which is the entire point of the file.
 *
 * The same reasoning applies to the browser version, the commit and the run id:
 * every one is observed or injected, never assumed.
 */

import { mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import type {
  FullConfig,
  FullResult,
  Reporter,
  TestCase,
  TestResult,
} from '@playwright/test/reporter'
import {
  EXPECTED_CASES,
  EXPECTED_VIEWPORTS,
  NETWORK_ATTACHMENT,
  readEnv,
  type EnvContext,
  type NetworkRecord,
} from './contract.js'

interface FixtureEvidenceFile {
  readonly fixture?: {
    readonly id?: string
    readonly schemaVersion?: number
    readonly byteLength?: number
    readonly sha256?: string
  }
  readonly database?: {
    readonly partyCount?: number
    readonly accountCount?: number
    readonly sequenceNextValue?: number
  }
}

/** The one company the authorized profile is bound to. */
const GDC_COMPANY_ID = '10000000-0000-0000-0000-000000000001'

interface CaseOutcome {
  readonly id: string
  readonly status: 'passed' | 'failed' | 'timedOut' | 'interrupted' | 'skipped'
}

export default class C01EvidenceReporter implements Reporter {
  private env: EnvContext | null = null
  private startedAtUtc = ''
  private readonly cases = new Map<string, CaseOutcome>()
  private readonly network: NetworkRecord[] = []
  private skipped = 0
  private browserName = ''
  private browserVersion = ''
  private failure: string | null = null

  onBegin(config: FullConfig): void {
    this.startedAtUtc = new Date().toISOString()
    try {
      this.env = readEnv()
    } catch (error) {
      // Recorded rather than thrown: a reporter that throws in onBegin hides the
      // real test output behind its own stack trace.
      this.failure = error instanceof Error ? error.message : String(error)
    }
    const project = config.projects[0]
    this.browserName = String(project?.use?.browserName ?? '')
  }

  onTestEnd(test: TestCase, result: TestResult): void {
    const id = caseIdOf(test.title)
    if (!id) return
    if (result.status === 'skipped') this.skipped += 1

    // Last status wins, so a retry that finally passes is reported as passed —
    // which is what the JUnit report will also say.
    this.cases.set(id, { id, status: result.status })

    for (const attachment of result.attachments) {
      if (attachment.name === NETWORK_ATTACHMENT && attachment.body) {
        try {
          const records = JSON.parse(attachment.body.toString('utf8')) as NetworkRecord[]
          for (const record of records) this.network.push(record)
        } catch (error) {
          this.failure ??= `network attachment from ${id} is not valid JSON: ${String(error)}`
        }
      }
      if (attachment.name === 'c01-browser-version' && attachment.body) {
        this.browserVersion = attachment.body.toString('utf8').trim()
      }
    }
  }

  async onEnd(result: FullResult): Promise<void> {
    if (this.failure) {
      process.stderr.write(`C01_EVIDENCE_NOT_WRITTEN: ${this.failure}\n`)
      return
    }
    const env = this.env
    if (!env) return

    // A missing or malformed fixture evidence file is an operator error, not a
    // crash: the raw ENOENT stack that this used to print buried the one thing
    // the reader needs to know, which is that the materializer step did not run.
    let fixture: Awaited<ReturnType<C01EvidenceReporter['readFixtureEvidence']>>
    try {
      fixture = await this.readFixtureEvidence(env)
    } catch (error) {
      process.stderr.write(
        `C01_EVIDENCE_NOT_WRITTEN: could not read the materialized fixture evidence at ` +
          `${env.fixtureEvidencePath}. The "Materialize and verify canonical C-01 fixture" ` +
          `step must run before the browser suite, and this reporter reads the fixture ` +
          `identity from it rather than hardcoding it. Cause: ${
            error instanceof Error ? error.message : String(error)
          }\n`,
      )
      return
    }
    await mkdir(env.artifactsDir, { recursive: true })

    // Ordinals are assigned here, not in the specs: they must be unique and
    // contiguous from 1 ACROSS the run, and no single spec can know that.
    this.network.forEach((record, index) => {
      record.ordinal = index + 1
    })

    const evidence = {
      schemaVersion: 1,
      suiteId: 'C01-REAL-BROWSER-E2E-V1',
      repository: 'sinangym/GoymenERP',
      testedCommit: env.testedCommit,
      runId: env.runId,
      composeProject: env.composeProject,
      fixture,
      browser: { name: this.browserName, version: this.browserVersion },
      viewports: [...EXPECTED_VIEWPORTS],
      cases: EXPECTED_CASES.map((id) => this.cases.get(id) ?? { id, status: 'skipped' }),
      skipped: this.skipped,
      startedAtUtc: this.startedAtUtc,
      completedAtUtc: new Date().toISOString(),
    }

    const network = {
      schemaVersion: 1,
      testedCommit: env.testedCommit,
      authorizedCompanyId: GDC_COMPANY_ID,
      // B-C01-001 is the case that actually asserts the GSP-only party is absent
      // from the authorized company's rendered page.
      //
      // This used to read B-C01-005 — the REFLOW case, which never looks at a
      // foreign record at all. The evidence therefore carried a claim no test
      // supported, which is the one thing an evidence file must never do. Codex
      // caught it (#173 P1-5).
      foreignRecordAbsent: this.cases.get('B-C01-001')?.status === 'passed',
      redacted: true,
      requests: this.network,
    }

    await writeJson(path.join(env.artifactsDir, 'c01-e2e-evidence.json'), evidence)
    await writeJson(path.join(env.artifactsDir, 'c01-e2e-network-summary.json'), network)
    process.stdout.write(
      `C01 evidence written: ${evidence.cases.length} cases, ${this.network.length} requests, ` +
        `run ${result.status}\n`,
    )
  }

  private async readFixtureEvidence(env: EnvContext) {
    const raw = await readFile(env.fixtureEvidencePath, 'utf8')
    const parsed = JSON.parse(raw) as FixtureEvidenceFile
    const artifact = parsed.fixture
    const database = parsed.database
    if (!artifact || !database) {
      throw new Error(
        `${env.fixtureEvidencePath} has no fixture/database evidence; the materializer ` +
          `step must run before the browser suite.`,
      )
    }
    // Composed from the two halves the materializer measured. Nothing here is a
    // literal from this repository.
    return {
      id: artifact.id,
      schemaVersion: artifact.schemaVersion,
      byteLength: artifact.byteLength,
      sha256: artifact.sha256,
      partyCount: database.partyCount,
      accountCount: database.accountCount,
      sequenceNextValue: database.sequenceNextValue,
    }
  }
}

/** `B-C01-007 continuation survives 503` → `B-C01-007`. */
function caseIdOf(title: string): string | null {
  const match = /^(B-C01-\d{3})(?:\s|$)/.exec(title)
  return match ? match[1] : null
}

async function writeJson(file: string, value: unknown): Promise<void> {
  await writeFile(file, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
}
