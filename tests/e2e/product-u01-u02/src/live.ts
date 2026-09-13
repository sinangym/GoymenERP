/**
 * The L lane for the Product screens: the LIVE API of a gate-verified environment,
 * through the same-origin Vite proxy, exactly as a user's browser would reach it.
 *
 * Nothing here fulfils a response. The 009 live tooling is REUSED where it is not
 * Cari-specific — `LiveRecorder` (records every `/api/v1/**` exchange with structural
 * redaction), `ActorProbe` (one direct request as a named actor), the evidence writers
 * and the withheld-delivery vocabulary. The one Cari-bound piece, the delivery
 * withholder, is bound to `/api/v1/counterparties` and to the Party identity; the
 * Product one below is the same mechanism narrowed to `/api/v1/products` and the
 * product identity (`card.product.id/code/version`, `isReplay`). If the 009 owner ever
 * parameterises the prefix, this class collapses to an import.
 *
 * Which environment: the one `manifest-validator.ts` bound the run to. A spec never
 * reads the manifest for itself; it takes the binding the runner established
 * (`liveEnvironment`) and writes its hashes into every evidence record.
 */
import type { APIResponse, Page } from '@playwright/test'
import type { WithheldMarker } from '../../cari-acceptance-009/src/lHarness'
import type { LiveBinding } from '../manifest-validator'
import { liveBinding, runId as browserRunId } from '../paths'

export { ActorProbe, LiveRecorder, lShot, writeLJson, failurePriority, evidenceErrorText } from '../../cari-acceptance-009/src/lHarness'
export type { LiveRecord, ActorProbeRecord } from '../../cari-acceptance-009/src/lHarness'
export type { LiveBinding } from '../manifest-validator'

/** The binding this worker JOINED in `../paths` (re-hashed and re-derived there); available only in the L lane. */
export function liveEnvironment(): LiveBinding {
  if (liveBinding === null) throw new Error('liveEnvironment() is only available in the L lane')
  return liveBinding
}

/**
 * The identity every L evidence record carries (contract step 3): the C010 BROWSER run
 * id and the environment's RESOURCE run/instance, distinct and explicitly linked, plus
 * all three file hashes — so a reader can tie each record to the exact snapshot.
 */
export function evidenceIdentity(binding: LiveBinding) {
  return {
    browserRunId,
    resourceRunId: binding.resourceRunId, resourceInstanceId: binding.resourceInstanceId,
    contractId: binding.contractId, taskId: binding.taskId, environmentName: binding.environmentName,
    manifestSha256: binding.manifestSha256, acceptedContractSha256: binding.acceptedContractSha256, sourceBindingSha256: binding.sourceBindingSha256,
    publisherVerifiedAtUtc: binding.verifiedAtUtc, frontendHead: binding.frontendHead,
    apiBaseUrl: binding.apiBaseUrl, uiOrigin: binding.uiOrigin,
    note: 'Live Product API through the same-origin Vite proxy of a publisher-verified, consumer-validated environment; not ERP acceptance by itself.',
  }
}

/** Identity read from a REAL mutation response — never the whole body. */
export interface ProductIdentity {
  readonly id: string | null
  readonly code: string | null
  readonly version: string | null
  readonly lifecycle: string | null
  readonly isReplay: boolean | null
}

export interface ProductWithheldDelivery {
  readonly marker: WithheldMarker
  readonly label: string
  readonly at: string
  readonly method: string
  readonly path: string
  /** Must be null on every Product request: the master is global. Recorded, not assumed. */
  readonly companyId: string | null
  readonly idempotencyKey: string | null
  readonly upstreamStatus: number
  readonly identity: ProductIdentity
  readonly deliveredToBrowser: false
  readonly browserOutcome: 'aborted-by-test'
  readonly upstreamError: string | null
}

interface Target {
  readonly label: string
  readonly method: 'POST' | 'PATCH'
  readonly expectedStatus: number
  readonly match: (pathname: string) => boolean
  done: boolean
}

export function productIdentityOf(body: unknown): ProductIdentity {
  const card = (body as { card?: { product?: Record<string, unknown> }; isReplay?: unknown } | null)?.card
  const product = card?.product ?? {}
  const str = (v: unknown) => (typeof v === 'string' ? v : null)
  const isReplay = (body as { isReplay?: unknown } | null)?.isReplay
  return {
    id: str(product.id), code: str(product.code), version: str(product.version), lifecycle: str(product.lifecycle),
    isReplay: typeof isReplay === 'boolean' ? isReplay : null,
  }
}

/**
 * Withholds the browser's delivery of the FIRST real command matching each target, once
 * each — the same mechanism as 009's `DeliveryWithholder`, narrowed to `/api/v1/products`.
 *
 * The request is re-issued to the real API through the same proxy with `route.fetch`
 * (same method, path, key and body the application produced). Only once the real
 * success and the real product identity have been SEEN is the browser's delivery cut.
 * The server has committed; the browser has an unknown outcome. Everything else — the
 * same-key retry included — is passed through untouched, so the retry is an ordinary
 * browser request that reaches the real API by itself and comes back as a real replay.
 */
export class ProductDeliveryWithholder {
  readonly withheld: ProductWithheldDelivery[] = []
  readonly passedThrough: { at: string; label: string; method: string; path: string; idempotencyKey: string | null }[] = []
  private constructor(private readonly targets: Target[]) {}

  static async install(
    page: Page,
    targets: readonly { label: string; method: 'POST' | 'PATCH'; expectedStatus: number; match: (pathname: string) => boolean }[],
  ): Promise<ProductDeliveryWithholder> {
    const withholder = new ProductDeliveryWithholder(targets.map((t) => ({ ...t, done: false })))
    await page.route(
      (url) => url.pathname.startsWith('/api/v1/products'),
      async (route) => {
        const request = route.request()
        const url = new URL(request.url())
        const headers = request.headers()
        const method = request.method().toUpperCase()
        const key = headers['idempotency-key'] ?? null
        const target = withholder.targets.find((t) => !t.done && t.method === method && key !== null && t.match(url.pathname))
        if (target === undefined) {
          const matched = withholder.targets.find((t) => t.method === method && t.match(url.pathname))
          if (matched !== undefined) withholder.passedThrough.push({ at: new Date().toISOString(), label: matched.label, method, path: url.pathname, idempotencyKey: key })
          await route.continue()
          return
        }
        target.done = true

        let upstream: APIResponse | null = null
        let failure: string | null = null
        try {
          // One real request, one answer: no retry, no redirect, or the evidence lies.
          upstream = await route.fetch({ maxRetries: 0, maxRedirects: 0 })
        } catch (error) {
          failure = String((error as Error)?.message ?? error).slice(0, 200)
        }
        const base = {
          label: target.label, at: new Date().toISOString(), method, path: url.pathname,
          companyId: headers['x-company-id'] ?? null, idempotencyKey: key,
          deliveredToBrowser: false as const, browserOutcome: 'aborted-by-test' as const,
        }
        if (upstream === null) {
          withholder.withheld.push({ ...base, marker: 'upstream_not_reached', upstreamStatus: 0, identity: productIdentityOf(null), upstreamError: failure })
          await route.abort('failed')
          return
        }
        let body: unknown = null
        try { body = await upstream.json() } catch { body = null }
        const identity = productIdentityOf(body)
        const ok = upstream.status() === target.expectedStatus && identity.id !== null && identity.version !== null
        withholder.withheld.push({
          ...base,
          marker: ok ? 'upstream_success_then_delivery_abort' : 'upstream_unexpected_result',
          upstreamStatus: upstream.status(), identity,
          upstreamError: ok ? null : `expected ${target.expectedStatus} with a product identity, got ${upstream.status()}`,
        })
        // The server has answered; the browser does not get to hear it.
        await route.abort('failed')
      },
    )
    return withholder
  }
}
