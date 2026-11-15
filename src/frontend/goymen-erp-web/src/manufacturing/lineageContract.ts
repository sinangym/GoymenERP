/**
 * Wire contract of the order's lineage (URT-S22, API-28, UI-18, URT-D63, KLN-D44):
 * `GET /api/v1/manufacturing/orders/{orderId}/lineage` (`OrderLineageViewV1`).
 *
 * Decoding fails CLOSED and is strict: every object carries exactly the contract's keys, every kilogram and share is the
 * server's lossless invariant text (never a JSON number, never converted, rounded or summed here), and the answer must
 * be the order that was asked for. The shares come only from the one backend share calculator: a `calculated` lineage
 * carries its total and a share on EVERY input line and no code; any other status carries no total, no share and the
 * calculator's code. The forward trace is the backward trace read the other way round, so it must name exactly the same
 * input lines with the same kg and share — a forward trace that does not match is not this contract. Upstream and
 * downstream outputs belong to OTHER orders; the order's own outputs belong to it. A lineage with one part that is not
 * this contract is `null`: a half-trusted lineage would show an input beside the wrong output.
 */

export const ORDER_LINEAGE_CONTRACT_VERSION = 1
/** `ProductionLineageShareCalculatorV1.ContractVersion`: the one share calculator this screen understands. */
export const ORDER_LINEAGE_CALCULATOR_VERSION = 1

export const ORDER_LINEAGE_SHARE_STATUSES = ['calculated', 'pending', 'unreadable'] as const
export type OrderLineageShareStatus = (typeof ORDER_LINEAGE_SHARE_STATUSES)[number]

export interface OrderLineageInputShare {
  readonly inputLabelId: string
  readonly consumedKg: string
  /** The input's share of the output; null unless the output's status is `calculated`. */
  readonly share: string | null
}

export interface OrderLineageOutput {
  readonly orderId: string
  readonly outputId: string
  readonly outputLabelId: string
  readonly genealogyTransformationId: string
  readonly shareStatus: OrderLineageShareStatus
  /** The calculator's code while the shares are not calculated; null when they are. */
  readonly shareCode: string | null
  /** The kg the shares are taken of; null unless calculated. */
  readonly totalConsumedKg: string | null
  readonly inputs: readonly OrderLineageInputShare[]
}

export interface OrderLineageFedOutput {
  readonly outputId: string
  readonly outputLabelId: string
  readonly shareStatus: OrderLineageShareStatus
  readonly consumedKg: string
  readonly share: string | null
}

export interface OrderLineageInput {
  readonly inputLabelId: string
  readonly outputs: readonly OrderLineageFedOutput[]
}

export interface OrderLineageView {
  readonly contractVersion: number
  readonly orderId: string
  readonly calculatorVersion: number
  /** A list met the server's read bound: what is shown is not the whole lineage. */
  readonly truncated: boolean
  readonly outputs: readonly OrderLineageOutput[]
  readonly inputs: readonly OrderLineageInput[]
  readonly upstream: readonly OrderLineageOutput[]
  readonly downstream: readonly OrderLineageOutput[]
}

const VIEW_KEYS = [
  'contractVersion',
  'orderId',
  'calculatorVersion',
  'truncated',
  'outputs',
  'inputs',
  'upstream',
  'downstream',
] as const
const OUTPUT_KEYS = [
  'orderId',
  'outputId',
  'outputLabelId',
  'genealogyTransformationId',
  'shareStatus',
  'shareCode',
  'totalConsumedKg',
  'inputs',
] as const
const INPUT_SHARE_KEYS = ['inputLabelId', 'consumedKg', 'share'] as const
const INPUT_KEYS = ['inputLabelId', 'outputs'] as const
const FED_OUTPUT_KEYS = ['outputId', 'outputLabelId', 'shareStatus', 'consumedKg', 'share'] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero; never negative. */
const AMOUNT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/** A share in the same text: from 0 to 1, unrounded. */
const SHARE = /^(0(\.[0-9]*[1-9])?|1)$/
/** A stable machine code: lower-case words joined by `_`, `-` or `.`. */
const CODE = /^[a-z][a-z0-9]*([._-][a-z0-9]+)*$/
const CODE_MAXIMUM_LENGTH = 128

type Json = Record<string, unknown>

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Json, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function nonNilGuid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID ? value : null
}

function sameId(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase()
}

function amount(value: unknown): string | null {
  return typeof value === 'string' && AMOUNT.test(value) ? value : null
}

/** `undefined` marks a value that is neither null nor a valid share. */
function optionalShare(value: unknown): string | null | undefined {
  if (value === null) return null
  return typeof value === 'string' && SHARE.test(value) ? value : undefined
}

/** `undefined` marks a value that is neither null nor a valid code. */
function optionalCode(value: unknown): string | null | undefined {
  if (value === null) return null
  return typeof value === 'string' && value.length <= CODE_MAXIMUM_LENGTH && CODE.test(value) ? value : undefined
}

/** `undefined` marks a value that is neither null nor a valid amount. */
function optionalAmount(value: unknown): string | null | undefined {
  if (value === null) return null
  return amount(value) ?? undefined
}

function isShareStatus(value: unknown): value is OrderLineageShareStatus {
  return typeof value === 'string' && (ORDER_LINEAGE_SHARE_STATUSES as readonly string[]).includes(value)
}

function decodeInputShare(value: unknown): OrderLineageInputShare | null {
  if (!isObject(value) || !hasExactKeys(value, INPUT_SHARE_KEYS)) return null
  const inputLabelId = nonNilGuid(value.inputLabelId)
  const consumedKg = amount(value.consumedKg)
  const share = optionalShare(value.share)
  if (inputLabelId === null || consumedKg === null || share === undefined) return null
  return { inputLabelId, consumedKg, share }
}

function decodeOutput(value: unknown): OrderLineageOutput | null {
  if (!isObject(value) || !hasExactKeys(value, OUTPUT_KEYS) || !isShareStatus(value.shareStatus)) return null
  const orderId = nonNilGuid(value.orderId)
  const outputId = nonNilGuid(value.outputId)
  const outputLabelId = nonNilGuid(value.outputLabelId)
  const genealogyTransformationId = nonNilGuid(value.genealogyTransformationId)
  const shareCode = optionalCode(value.shareCode)
  const totalConsumedKg = optionalAmount(value.totalConsumedKg)
  if (
    orderId === null ||
    outputId === null ||
    outputLabelId === null ||
    genealogyTransformationId === null ||
    shareCode === undefined ||
    totalConsumedKg === undefined ||
    !Array.isArray(value.inputs)
  ) {
    return null
  }
  const inputs: OrderLineageInputShare[] = []
  const seen = new Set<string>()
  for (const raw of value.inputs) {
    const input = decodeInputShare(raw)
    if (input === null) return null
    // One line per input label: a repeated label would be counted twice.
    const key = input.inputLabelId.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
    inputs.push(input)
  }
  const shareStatus = value.shareStatus
  if (shareStatus === 'calculated') {
    // The total and a share on every line, and no code beside them.
    if (totalConsumedKg === null || shareCode !== null || inputs.some((input) => input.share === null)) return null
  } else if (totalConsumedKg !== null || shareCode === null || inputs.some((input) => input.share !== null)) {
    // No total, no share — never a guess — and the calculator's code.
    return null
  }
  return { orderId, outputId, outputLabelId, genealogyTransformationId, shareStatus, shareCode, totalConsumedKg, inputs }
}

/** The outputs, or `null` when one is not this contract, does not `belong`, or repeats an output id. */
function decodeOutputs(value: unknown, belongs: (output: OrderLineageOutput) => boolean): OrderLineageOutput[] | null {
  if (!Array.isArray(value)) return null
  const outputs: OrderLineageOutput[] = []
  const seen = new Set<string>()
  for (const raw of value) {
    const output = decodeOutput(raw)
    if (output === null || !belongs(output)) return null
    const key = output.outputId.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
    outputs.push(output)
  }
  return outputs
}

function decodeFedOutput(value: unknown): OrderLineageFedOutput | null {
  if (!isObject(value) || !hasExactKeys(value, FED_OUTPUT_KEYS) || !isShareStatus(value.shareStatus)) return null
  const outputId = nonNilGuid(value.outputId)
  const outputLabelId = nonNilGuid(value.outputLabelId)
  const consumedKg = amount(value.consumedKg)
  const share = optionalShare(value.share)
  if (outputId === null || outputLabelId === null || consumedKg === null || share === undefined) return null
  return { outputId, outputLabelId, shareStatus: value.shareStatus, consumedKg, share }
}

/**
 * The forward trace, or `null` when it is not exactly the backward trace read the other way round: every fed output is
 * one of the order's outputs with the same label and status, holding a line of this input with the same kg and share,
 * and every line of every output appears once.
 */
function decodeInputs(value: unknown, outputs: readonly OrderLineageOutput[]): OrderLineageInput[] | null {
  if (!Array.isArray(value)) return null
  const byOutputId = new Map(outputs.map((output) => [output.outputId.toLowerCase(), output]))
  const expectedLines = outputs.reduce((total, output) => total + output.inputs.length, 0)
  const inputs: OrderLineageInput[] = []
  const seenInputs = new Set<string>()
  let lines = 0
  for (const raw of value) {
    if (!isObject(raw) || !hasExactKeys(raw, INPUT_KEYS) || !Array.isArray(raw.outputs)) return null
    const inputLabelId = nonNilGuid(raw.inputLabelId)
    if (inputLabelId === null || seenInputs.has(inputLabelId.toLowerCase())) return null
    seenInputs.add(inputLabelId.toLowerCase())
    const fed: OrderLineageFedOutput[] = []
    const seenOutputs = new Set<string>()
    for (const rawFed of raw.outputs) {
      const output = decodeFedOutput(rawFed)
      if (output === null || seenOutputs.has(output.outputId.toLowerCase())) return null
      seenOutputs.add(output.outputId.toLowerCase())
      const source = byOutputId.get(output.outputId.toLowerCase())
      const line = source?.inputs.find((input) => sameId(input.inputLabelId, inputLabelId))
      if (
        source === undefined ||
        line === undefined ||
        !sameId(source.outputLabelId, output.outputLabelId) ||
        source.shareStatus !== output.shareStatus ||
        line.consumedKg !== output.consumedKg ||
        line.share !== output.share
      ) {
        return null
      }
      fed.push(output)
    }
    // An input label that fed nothing is not part of the forward trace.
    if (fed.length === 0) return null
    lines += fed.length
    inputs.push({ inputLabelId, outputs: fed })
  }
  return lines === expectedLines ? inputs : null
}

/** The lineage of `requestedOrderId`, version 1, or `null` when any part of it is not this contract. */
export function decodeOrderLineage(value: unknown, requestedOrderId: string): OrderLineageView | null {
  if (!isObject(value) || !hasExactKeys(value, VIEW_KEYS)) return null
  if (value.contractVersion !== ORDER_LINEAGE_CONTRACT_VERSION) return null
  if (value.calculatorVersion !== ORDER_LINEAGE_CALCULATOR_VERSION) return null
  if (typeof value.truncated !== 'boolean') return null
  const orderId = nonNilGuid(value.orderId)
  if (orderId === null || !sameId(orderId, requestedOrderId)) return null
  const outputs = decodeOutputs(value.outputs, (output) => sameId(output.orderId, orderId))
  const upstream = decodeOutputs(value.upstream, (output) => !sameId(output.orderId, orderId))
  const downstream = decodeOutputs(value.downstream, (output) => !sameId(output.orderId, orderId))
  if (outputs === null || upstream === null || downstream === null) return null
  const inputs = decodeInputs(value.inputs, outputs)
  if (inputs === null) return null
  return {
    contractVersion: ORDER_LINEAGE_CONTRACT_VERSION,
    orderId,
    calculatorVersion: ORDER_LINEAGE_CALCULATOR_VERSION,
    truncated: value.truncated,
    outputs,
    inputs,
    upstream,
    downstream,
  }
}
