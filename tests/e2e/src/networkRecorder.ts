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

/** A real response fetched and parked at the transport, awaiting delivery. */
export interface HeldResponse {
  /** Resolves once the real response exists and is genuinely being withheld. */
  readonly ready: Promise<void>
  readonly release: () => void
  /**
   * True only once the PAGE observed the response.
   *
   * This used to report whether `route.fulfill` resolved, which proves nothing
   * about the client: measured on the running app, a company switch ABORTS the
   * in-flight read, fulfil still resolves, and the page never receives anything.
   * B-C01-003 was passing on that signal while nothing was delivered.
   */
  readonly wasDelivered: () => boolean
  /** The transport error if the read was cancelled, e.g. `net::ERR_ABORTED`. */
  readonly abortError: () => string | null
}

export class NetworkRecorder {
  private readonly records: NetworkRecord[] = []

  private constructor(private readonly page: Page) {}

  static async attach(page: Page): Promise<NetworkRecorder> {
    const recorder = new NetworkRecorder(page)
    page.on('response', (response) => {
      const url = response.url()
      if (!url.includes(API_PREFIX)) return
      // A fulfilled (injected) response is recorded by the injector itself, so it
      // is not double counted here.
      //
      // This is a COUNT per URL, not a set. A retry re-issues the SAME cursor URL,
      // so a set made the recorder skip the successful retry response forever —
      // which is precisely why `retry-200` had to be asserted by hand instead of
      // observed (#173 P1-3). Aborts never produce a response event, so they must
      // not add to the count or they would swallow the next real one.
      const pending = recorder.injectedResponses.get(url) ?? 0
      if (pending > 0) {
        recorder.injectedResponses.set(url, pending - 1)
        return
      }
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
      const path = redactPath(url)
      // A successful counterparty read that the spec declared to be a retry
      // recovery is recorded as such — observed, never asserted in advance.
      if (
        outcome === 'real-200' &&
        path === '/api/v1/counterparties' &&
        recorder.pendingRetryOrdinal !== null &&
        url === recorder.failedReadUrl
      ) {
        const ordinal = recorder.pendingRetryOrdinal
        recorder.pendingRetryOrdinal = null
        recorder.push(path, 'retry-200', ordinal)
        return
      }
      recorder.push(path, outcome, 0)
    })
    return recorder
  }

  /** url -> number of injected responses still expected on the response channel. */
  private readonly injectedResponses = new Map<string, number>()

  private expectInjectedResponse(url: string): void {
    this.injectedResponses.set(url, (this.injectedResponses.get(url) ?? 0) + 1)
  }

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
  ): Promise<() => boolean> {
    let used = false
    await this.page.route('**/api/v1/counterparties*', async (route: Route) => {
      if (used) return route.fallback()
      used = true
      const url = route.request().url()
      this.failedReadUrl = url
      if (outcome !== 'injected-abort') this.expectInjectedResponse(url)
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
    // Returned so a caller can wait for the fault to have been CONSUMED. Watching
    // the retry control go disabled was the obvious signal and it is unreliable:
    // an injected response settles fast enough that the transient state is missed
    // between polls, and the alert from the previous fault is still on screen, so
    // a bare visibility check is satisfied by the wrong thing.
    return () => used
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
  async dropNextCounterpartyRead(): Promise<() => boolean> {
    let used = false
    await this.page.route('**/api/v1/counterparties*', async (route: Route) => {
      if (used) return route.fallback()
      used = true
      const url = route.request().url()
      this.failedReadUrl = url
      // An abort produces no response event, so nothing is expected on that channel.
      this.push(redactPath(url), 'injected-offline', 0)
      return route.abort('internetdisconnected')
    })
    return () => used
  }

  /**
   * Fetch the next counterparty response for real, then WITHHOLD its delivery
   * until the returned function is called.
   *
   * This is the difference between proving something and appearing to. An earlier
   * version slept before `route.continue()`, which delays the REQUEST — the
   * response then arrives into a context that has not moved, so nothing about
   * stale delivery is exercised. Codex was right to block on it (#173 P1-2).
   *
   * Here the real server response is obtained first and held at the transport, so
   * the caller can commit a different company before releasing it.
   *
   * WHAT ACTUALLY HAPPENS THEN, measured rather than assumed: the switch CANCELS
   * the in-flight read, so the release lands on a request that is already gone and
   * the page observes `net::ERR_ABORTED`. `wasDelivered()` reports the page's own
   * account of this, which is why it is a page-side listener and not the result of
   * `route.fulfill` — fulfil resolves even when nobody is listening.
   *
   * So this helper sets up a delivery attempt; whether delivery OCCURS is a
   * measurement, not a guarantee, and the caller must assert which one it got.
   */
  async holdNextCounterpartyResponse(): Promise<HeldResponse> {
    let release: () => void = () => {}
    const held = new Promise<void>((resolve) => {
      release = resolve
    })
    // READY LATCH. Without it the caller gets `release` immediately and can call
    // it before the handler has even reached `route.fetch()` — the response then
    // lands while the context is still the old one and the case silently proves
    // nothing. Codex's post-merge audit on #173 was right to flag it. The caller
    // must await `ready`, which resolves only once the real response is fetched
    // and parked.
    let markReady: () => void = () => {}
    const ready = new Promise<void>((resolve) => {
      markReady = resolve
    })
    let delivered = false
    let abortError: string | null = null
    let heldUrl: string | null = null
    // Page-side observation, not interceptor-side.
    this.page.on('response', (response) => {
      if (heldUrl !== null && response.url() === heldUrl) delivered = true
    })
    this.page.on('requestfailed', (request) => {
      if (heldUrl !== null && request.url() === heldUrl) {
        abortError = request.failure()?.errorText ?? 'unknown'
      }
    })
    let used = false
    await this.page.route('**/api/v1/counterparties*', async (route: Route) => {
      if (used) return route.fallback()
      used = true
      heldUrl = route.request().url()
      this.expectInjectedResponse(heldUrl)
      const response = await route.fetch()
      const body = await response.body()
      markReady()
      await held
      this.push(redactPath(heldUrl), 'injected-delay', 0)
      try {
        await route.fulfill({ response, body })
      } catch {
        // The request was already gone; the page-side listeners are the authority.
      }
    })
    return {
      ready,
      release: () => release(),
      wasDelivered: () => delivered,
      abortError: () => abortError,
    }
  }

  /**
   * Classify the next SUCCESSFUL counterparty read as the recovery of a retry.
   *
   * Previously this pushed a `retry-200` record unconditionally, which meant the
   * evidence asserted a recovery the recorder had never seen (#173 P1-3). Now the
   * outcome is only written when a real 200 actually arrives.
   */
  expectRetrySuccess(retryOrdinal: number): void {
    this.pendingRetryOrdinal = retryOrdinal
  }

  private pendingRetryOrdinal: number | null = null

  /**
   * The exact URL of the read that failed, so a recovery is bound to THAT read.
   *
   * Classifying "the next 200 on /counterparties" was too loose: any other
   * successful read arriving first would have been mislabelled as the recovery
   * (#173 post-merge audit, point 4). The cursor is part of the URL, so matching
   * it also binds the retry to the same cursor rather than to a fresh page one.
   */
  private failedReadUrl: string | null = null

  async detach(testInfo: TestInfo): Promise<void> {
    await this.page.unrouteAll({ behavior: 'ignoreErrors' })
    await testInfo.attach(NETWORK_ATTACHMENT, {
      body: Buffer.from(JSON.stringify(this.records), 'utf8'),
      contentType: 'application/json',
    })
  }
}
