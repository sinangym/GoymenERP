/**
 * Records what the browser actually asked the API for, and what came back.
 *
 * Two things make this more than logging.
 *
 * First, REDACTION IS STRUCTURAL. A path only enters the record through
 * `redactPath`, which returns `URL.pathname` and therefore cannot carry a query
 * string, a cursor, or a token. The validator re-checks this, but the guarantee
 * is here: there is no code path that appends a query.
 *
 * Second, FAULTS ARE INJECTED AT THE RESPONSE STAGE, through
 * `page.route`/`route.fulfill` and CDP, not by patching `window.fetch`. The app
 * binds fetch at module load, so a page-level patch never reaches it — that was
 * measured, and it is why the jsdom attempts at B-C01-003 could only ever prove
 * an abort. A fault injected here is delivered to the running client the same
 * way the server would deliver it.
 */

import type { Page, Route, TestInfo } from '@playwright/test'
import {
  NETWORK_ATTACHMENT,
  redactPath,
  type NetworkOutcome,
  type NetworkRecord,
} from './contract.js'

const API_PREFIX = '/api/v1/'

export class NetworkRecorder {
  private readonly records: NetworkRecord[] = []

  /** Outcomes the spec has declared for the NEXT matching counterparty read. */
  private readonly queued: NetworkOutcome[] = []

  private constructor(private readonly page: Page) {}

  static async attach(page: Page): Promise<NetworkRecorder> {
    const recorder = new NetworkRecorder(page)
    page.on('response', (response) => {
      const url = response.url()
      if (!url.includes(API_PREFIX)) return
      // A fulfilled (injected) response is recorded by the injector itself, so it
      // is not double counted here.
      if (recorder.injectedUrls.has(url)) return
      const status = response.status()
      const outcome: NetworkOutcome | null =
        status === 200
          ? 'real-200'
          : status === 401
            ? 'real-401'
            : status === 403
              ? 'real-403'
              : status === 400
                ? 'real-400-invalid-cursor'
                : null
      if (outcome === null) return
      recorder.push(redactPath(url), outcome, 0)
    })
    return recorder
  }

  private readonly injectedUrls = new Set<string>()

  private push(path: string, outcome: NetworkOutcome, retryOrdinal: number): void {
    // `ordinal` is a placeholder: the reporter renumbers every record across the
    // whole run, because uniqueness and contiguity are run-wide properties.
    this.records.push({ method: 'GET', path, ordinal: 0, outcome, retryOrdinal })
  }

  /** Record an outcome the spec produced by other means (offline, abort). */
  note(path: string, outcome: NetworkOutcome, retryOrdinal = 0): void {
    this.push(path, outcome, retryOrdinal)
  }

  /**
   * Fail the next counterparty read once, at the response stage.
   *
   * `once` is deliberate: the retry that follows must reach the real server, so
   * that `retry-200` is evidence of a real recovery rather than of another
   * injected response.
   */
  async failNextCounterpartyRead(
    outcome: Extract<NetworkOutcome, 'injected-500' | 'injected-503' | 'injected-abort'>,
    options: { readonly delayMs?: number } = {},
  ): Promise<void> {
    let used = false
    await this.page.route('**/api/v1/counterparties*', async (route: Route) => {
      if (used) return route.fallback()
      used = true
      const url = route.request().url()
      this.injectedUrls.add(url)
      if (options.delayMs) await new Promise((resolve) => setTimeout(resolve, options.delayMs))
      this.push(redactPath(url), outcome, 0)
      if (outcome === 'injected-abort') return route.abort('failed')
      const status = outcome === 'injected-500' ? 500 : 503
      return route.fulfill({
        status,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          type: 'about:blank',
          title: status === 500 ? 'internal_error' : 'service_unavailable',
          status,
        }),
      })
    })
  }

  /**
   * Fail the next counterparty read the way a dropped connection does.
   *
   * `BrowserContext.setOffline` was the obvious choice and it is the WRONG one
   * here: measured against the running app, a read started while offline stays
   * PENDING and then completes successfully the moment connectivity returns. That
   * produces a delayed success, not a failure, so the case it was meant to prove
   * silently proved nothing. Aborting with `internetdisconnected` fails the read
   * deterministically at the transport layer, which is the condition the
   * continuation allowlist actually classifies.
   */
  async dropNextCounterpartyRead(): Promise<void> {
    let used = false
    await this.page.route('**/api/v1/counterparties*', async (route: Route) => {
      if (used) return route.fallback()
      used = true
      const url = route.request().url()
      this.injectedUrls.add(url)
      this.push(redactPath(url), 'injected-offline', 0)
      return route.abort('internetdisconnected')
    })
  }

  /** Hold the next counterparty read, then let the real one through. */
  async delayNextCounterpartyRead(delayMs: number): Promise<void> {
    let used = false
    await this.page.route('**/api/v1/counterparties*', async (route: Route) => {
      if (used) return route.fallback()
      used = true
      const url = route.request().url()
      await new Promise((resolve) => setTimeout(resolve, delayMs))
      this.push(redactPath(url), 'injected-delay', 0)
      this.injectedUrls.add(url)
      return route.continue()
    })
  }

  /** Mark the read that succeeded after a user-driven retry. */
  noteRetrySuccess(retryOrdinal: number): void {
    this.push('/api/v1/counterparties', 'retry-200', retryOrdinal)
  }

  async detach(testInfo: TestInfo): Promise<void> {
    await this.page.unrouteAll({ behavior: 'ignoreErrors' })
    await testInfo.attach(NETWORK_ATTACHMENT, {
      body: Buffer.from(JSON.stringify(this.records), 'utf8'),
      contentType: 'application/json',
    })
  }
}
