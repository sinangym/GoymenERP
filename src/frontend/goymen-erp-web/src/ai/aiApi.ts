/**
 * Plan AI-3 — AI runtime transport. The committed shell company travels as `X-Company-Id` (request context), never in
 * the body; the server resolves and checks it again. A 200 is data only when it decodes; anything else is the classified
 * outcome, with the ProblemDetails body kept so the panel can read its stable `code`.
 */

import type { ViewOutcome } from '../shell/uiState'
import {
  AI_ASK_PATH,
  AI_ASK_MODE,
  AI_CAPABILITIES_PATH,
  decodeAiAskAnswer,
  decodeAiCapabilities,
  type AiAskAnswer,
  type AiAskRequest,
  type AiCapabilities,
} from './aiContract'
import { AI_DOCUMENT_DRAFT_PATH, decodeDocumentDraft, type DocumentDraft, type DraftRequest } from './documentDraftContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

async function send<T>(
  path: string,
  init: RequestInit,
  decode: (body: unknown) => T | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { ...init, signal })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decode(body)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

export function fetchAiCapabilities(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<AiCapabilities>> {
  return send(AI_CAPABILITIES_PATH, { headers: headers(companyId) }, decodeAiCapabilities, signal)
}

export function askAi(request: AiAskRequest, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<AiAskAnswer>> {
  return send(
    AI_ASK_PATH,
    {
      method: 'POST',
      headers: { ...headers(companyId), 'Content-Type': 'application/json' },
      body: JSON.stringify(request.mode === AI_ASK_MODE.generalChat ? { question: request.question, mode: AI_ASK_MODE.generalChat } : {
        question: request.question,
        toolId: request.toolId,
        toolVersion: request.toolVersion,
        arguments: request.arguments,
      }),
    },
    body => decodeAiAskAnswer(body, request.mode ?? AI_ASK_MODE.erp),
    signal,
  )
}

/** WP-B3: a document becomes a draft only; the response is data when it decodes (and never claims a created record). */
export function createDocumentDraft(
  request: DraftRequest,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DocumentDraft>> {
  return send(
    AI_DOCUMENT_DRAFT_PATH,
    {
      method: 'POST',
      headers: { ...headers(companyId), 'Content-Type': 'application/json' },
      body: JSON.stringify(
        'contentBase64' in request
          ? { draftKind: request.draftKind, mediaType: request.mediaType, contentBase64: request.contentBase64 }
          : { draftKind: request.draftKind, text: request.text, textSource: request.textSource },
      ),
    },
    decodeDocumentDraft,
    signal,
  )
}
