/**
 * Plan AI-3 — AI runtime wire contract (READ only, fake/local provider; no external provider, OPEN-020/AI-D4).
 *
 *   GET  /api/v1/ai/capabilities → AiCapabilitiesV1 { availability, panelAvailable, tools: [{ toolId, version, ownerModule, dataClass }] }
 *   POST /api/v1/ai/ask          → AiAskResponseV1  { outcome, toolResults: [{ toolId, toolVersion, outcome, contentJson, source }], answer }
 *
 * WP-A1: `answer` is the gateway-composed text with its citations (every number in it comes from a cited source); it is
 * present exactly when `outcome` is `answered` and null when `outcome` is `cannot_answer`. Every citation must name a
 * source the response itself released.
 *
 * The company is request context (`X-Company-Id`), never a body member and never a tool argument: the server refuses a
 * `companyId` argument. These decoders take EXACTLY the contract's members and fail closed (null → `malformed`) on
 * anything else, so nothing the contract does not name (a provider id, a prompt, a raw row) reaches the screen.
 */

export const AI_CAPABILITIES_PATH = '/ai/capabilities'
export const AI_ASK_PATH = '/ai/ask'

export const AI_AVAILABILITY = { disabled: 'disabled', unavailable: 'unavailable', ready: 'ready' } as const
export type AiAvailability = (typeof AI_AVAILABILITY)[keyof typeof AI_AVAILABILITY]

export const AI_DATA_CLASSES = ['Public', 'Internal', 'Confidential', 'StrictlyConfidential'] as const
export type AiDataClass = (typeof AI_DATA_CLASSES)[number]

export const AI_TOOL_RESULT_OUTCOME = { succeeded: 'succeeded', notFound: 'not_found' } as const
export type AiToolResultOutcome = (typeof AI_TOOL_RESULT_OUTCOME)[keyof typeof AI_TOOL_RESULT_OUTCOME]

/** Stable problem codes of POST /ai/ask (AiEndpoints). */
export const AI_PROBLEM_CODE = {
  disabled: 'ai_disabled',
  toolUnavailable: 'ai_tool_unavailable',
  requestInvalid: 'ai_request_invalid',
  featureDisabled: 'ai_feature_disabled',
  budgetExhausted: 'ai_budget_exhausted',
} as const

export const AI_ASK_OUTCOME = { answered: 'answered', cannotAnswer: 'cannot_answer' } as const
export type AiAskOutcome = (typeof AI_ASK_OUTCOME)[keyof typeof AI_ASK_OUTCOME]

/** The fields the server names in `invalidFields` of an `ai_request_invalid` problem. */
export const AI_INVALID_FIELDS = ['question', 'tool', 'arguments'] as const
export type AiInvalidField = (typeof AI_INVALID_FIELDS)[number]

export interface AiToolCapability {
  readonly toolId: string
  readonly version: string
  readonly ownerModule: string
  readonly dataClass: AiDataClass
}

export interface AiCapabilities {
  readonly availability: AiAvailability
  readonly panelAvailable: boolean
  readonly tools: readonly AiToolCapability[]
}

export interface AiToolSource {
  readonly targetKind: string
  readonly targetId: string
  readonly title: string
  readonly sourceVersion: string
  readonly asOfUtc: string
  readonly dataClass: AiDataClass
}

export interface AiToolResult {
  readonly toolId: string
  readonly toolVersion: string
  readonly outcome: AiToolResultOutcome
  readonly contentJson: string | null
  readonly source: AiToolSource | null
}

export interface AiCitation {
  readonly toolId: string
  readonly toolVersion: string
  readonly targetKind: string
  readonly targetId: string
  readonly title: string
  readonly sourceVersion: string
  readonly asOfUtc: string
}

export interface AiComposedAnswer {
  readonly text: string
  readonly citations: readonly AiCitation[]
  readonly modelDeployment: string
  readonly policyVersion: string
}

export interface AiAskAnswer {
  readonly outcome: AiAskOutcome
  readonly toolResults: readonly AiToolResult[]
  readonly answer: AiComposedAnswer | null
}

export interface AiAskRequest {
  readonly question: string
  readonly toolId: string | null
  readonly toolVersion: string | null
  readonly arguments: Readonly<Record<string, unknown>> | null
}

const CAPABILITIES_KEYS = ['availability', 'panelAvailable', 'tools'] as const
const TOOL_KEYS = ['toolId', 'version', 'ownerModule', 'dataClass'] as const
const ANSWER_KEYS = ['outcome', 'toolResults', 'answer'] as const
const COMPOSED_KEYS = ['text', 'citations', 'modelDeployment', 'policyVersion'] as const
const CITATION_KEYS = ['toolId', 'toolVersion', 'targetKind', 'targetId', 'title', 'sourceVersion', 'asOfUtc'] as const
const RESULT_KEYS = ['toolId', 'toolVersion', 'outcome', 'contentJson', 'source'] as const
const SOURCE_KEYS = ['targetKind', 'targetId', 'title', 'sourceVersion', 'asOfUtc', 'dataClass'] as const
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const TOOL_ID = /^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+\.v[1-9][0-9]*$/
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|\+00:00)$/

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const members = Object.keys(value)
  if (members.length !== keys.length || !members.every((member) => keys.includes(member))) return null
  return value as Obj
}

const isText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const isDataClass = (value: unknown): value is AiDataClass =>
  typeof value === 'string' && (AI_DATA_CLASSES as readonly string[]).includes(value)
const isAvailability = (value: unknown): value is AiAvailability =>
  typeof value === 'string' && (Object.values(AI_AVAILABILITY) as string[]).includes(value)
const isResultOutcome = (value: unknown): value is AiToolResultOutcome =>
  typeof value === 'string' && (Object.values(AI_TOOL_RESULT_OUTCOME) as string[]).includes(value)

function decodeTool(value: unknown): AiToolCapability | null {
  const tool = exact(value, TOOL_KEYS)
  if (tool === null || !isText(tool.toolId) || !TOOL_ID.test(tool.toolId) || !isText(tool.version)) return null
  if (!isText(tool.ownerModule) || !isDataClass(tool.dataClass)) return null
  return { toolId: tool.toolId, version: tool.version, ownerModule: tool.ownerModule, dataClass: tool.dataClass }
}

export function decodeAiCapabilities(value: unknown): AiCapabilities | null {
  const body = exact(value, CAPABILITIES_KEYS)
  if (body === null || !isAvailability(body.availability) || typeof body.panelAvailable !== 'boolean') return null
  if (!Array.isArray(body.tools)) return null
  const tools: AiToolCapability[] = []
  const seen = new Set<string>()
  for (const entry of body.tools as unknown[]) {
    const tool = decodeTool(entry)
    if (tool === null) return null
    const identity = `${tool.toolId}@${tool.version}`
    if (seen.has(identity)) return null
    seen.add(identity)
    tools.push(tool)
  }
  // The server lists tools only when the panel is usable; tools on a closed panel are a contract breach.
  if ((body.availability !== AI_AVAILABILITY.ready || !body.panelAvailable) && tools.length > 0) return null
  if (body.availability !== AI_AVAILABILITY.ready && body.panelAvailable) return null
  return { availability: body.availability, panelAvailable: body.panelAvailable, tools }
}

function decodeSource(value: unknown): AiToolSource | null {
  const source = exact(value, SOURCE_KEYS)
  if (source === null || !isText(source.targetKind) || typeof source.targetId !== 'string' || !UUID.test(source.targetId)) return null
  if (!isText(source.title) || !isText(source.sourceVersion)) return null
  if (typeof source.asOfUtc !== 'string' || !UTC_INSTANT.test(source.asOfUtc) || !isDataClass(source.dataClass)) return null
  return {
    targetKind: source.targetKind,
    targetId: source.targetId,
    title: source.title,
    sourceVersion: source.sourceVersion,
    asOfUtc: source.asOfUtc,
    dataClass: source.dataClass,
  }
}

function decodeResult(value: unknown): AiToolResult | null {
  const result = exact(value, RESULT_KEYS)
  if (result === null || !isText(result.toolId) || !TOOL_ID.test(result.toolId) || !isText(result.toolVersion)) return null
  if (!isResultOutcome(result.outcome)) return null
  if (result.outcome === AI_TOOL_RESULT_OUTCOME.notFound) {
    // A not-found result discloses nothing: no content, no source.
    if (result.contentJson !== null || result.source !== null) return null
    return { toolId: result.toolId, toolVersion: result.toolVersion, outcome: result.outcome, contentJson: null, source: null }
  }
  if (result.contentJson !== null && typeof result.contentJson !== 'string') return null
  const source = result.source === null ? null : decodeSource(result.source)
  if (result.source !== null && source === null) return null
  return {
    toolId: result.toolId,
    toolVersion: result.toolVersion,
    outcome: result.outcome,
    contentJson: result.contentJson as string | null,
    source,
  }
}

function decodeCitation(value: unknown, released: readonly AiToolResult[]): AiCitation | null {
  const citation = exact(value, CITATION_KEYS)
  if (citation === null || !isText(citation.toolId) || !isText(citation.toolVersion) || !isText(citation.targetKind)) return null
  if (typeof citation.targetId !== 'string' || !UUID.test(citation.targetId) || !isText(citation.title)) return null
  if (!isText(citation.sourceVersion) || typeof citation.asOfUtc !== 'string' || !UTC_INSTANT.test(citation.asOfUtc)) return null
  // A citation may only point at a source this response released.
  const matches = released.some(
    (result) =>
      result.source !== null &&
      result.toolId === citation.toolId &&
      result.toolVersion === citation.toolVersion &&
      result.source.targetKind === citation.targetKind &&
      result.source.targetId === citation.targetId &&
      result.source.sourceVersion === citation.sourceVersion,
  )
  if (!matches) return null
  return {
    toolId: citation.toolId,
    toolVersion: citation.toolVersion,
    targetKind: citation.targetKind,
    targetId: citation.targetId,
    title: citation.title,
    sourceVersion: citation.sourceVersion,
    asOfUtc: citation.asOfUtc,
  }
}

function decodeComposed(value: unknown, released: readonly AiToolResult[]): AiComposedAnswer | null {
  const composed = exact(value, COMPOSED_KEYS)
  if (composed === null || !isText(composed.text) || !Array.isArray(composed.citations)) return null
  if (!isText(composed.modelDeployment) || !isText(composed.policyVersion)) return null
  const citations: AiCitation[] = []
  for (const entry of composed.citations as unknown[]) {
    const citation = decodeCitation(entry, released)
    if (citation === null) return null
    citations.push(citation)
  }
  // A sourced answer always cites at least one source.
  if (citations.length === 0) return null
  return { text: composed.text, citations, modelDeployment: composed.modelDeployment, policyVersion: composed.policyVersion }
}

export function decodeAiAskAnswer(value: unknown): AiAskAnswer | null {
  const body = exact(value, ANSWER_KEYS)
  if (body === null || !Array.isArray(body.toolResults)) return null
  if (body.outcome !== AI_ASK_OUTCOME.answered && body.outcome !== AI_ASK_OUTCOME.cannotAnswer) return null
  const toolResults: AiToolResult[] = []
  for (const entry of body.toolResults as unknown[]) {
    const result = decodeResult(entry)
    if (result === null) return null
    toolResults.push(result)
  }
  if (body.outcome === AI_ASK_OUTCOME.cannotAnswer) {
    return body.answer === null ? { outcome: AI_ASK_OUTCOME.cannotAnswer, toolResults, answer: null } : null
  }
  const answer = decodeComposed(body.answer, toolResults)
  return answer === null ? null : { outcome: AI_ASK_OUTCOME.answered, toolResults, answer }
}

/** The stable `code` of a ProblemDetails body, or null. */
export function problemCode(body: unknown): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const code = (body as Obj).code
  return typeof code === 'string' ? code : null
}

/** The known `invalidFields` of an `ai_request_invalid` problem; unknown names are dropped, never shown. */
export function problemInvalidFields(body: unknown): readonly AiInvalidField[] {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return []
  const fields = (body as Obj).invalidFields
  if (!Array.isArray(fields)) return []
  return AI_INVALID_FIELDS.filter((field) => (fields as unknown[]).includes(field))
}

/** Client-side shape check only (the server is authoritative): arguments are empty or one JSON object. */
export function parseToolArguments(text: string): Readonly<Record<string, unknown>> | null | 'invalid' {
  const trimmed = text.trim()
  if (trimmed === '') return null
  try {
    const parsed: unknown = JSON.parse(trimmed)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return 'invalid'
    return parsed as Record<string, unknown>
  } catch {
    return 'invalid'
  }
}
