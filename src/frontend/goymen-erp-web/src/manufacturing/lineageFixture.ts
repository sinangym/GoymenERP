/**
 * Test-only synthetic `OrderLineageViewV1` bodies in the server's JSON shape (URT-S22). Component data, never real
 * fixture evidence.
 *
 * The default lineage: output one takes 75 kg of input label A and 25 kg of input label B (shares 0.75 and 0.25),
 * output two takes 40 kg of input label A (share 1); input label A was produced by another order (upstream) and output
 * label one went into another order (downstream).
 */

import { ORDER } from './testHarness'

export const OUTPUT_1 = 'f2000000-0000-4000-8000-000000000001'
export const OUTPUT_2 = 'f2000000-0000-4000-8000-000000000002'
export const OUTPUT_LABEL_1 = 'f2000000-0000-4000-8000-000000000011'
export const OUTPUT_LABEL_2 = 'f2000000-0000-4000-8000-000000000012'
export const INPUT_LABEL_A = 'f2000000-0000-4000-8000-000000000021'
export const INPUT_LABEL_B = 'f2000000-0000-4000-8000-000000000022'
export const TRANSFORMATION_1 = 'f2000000-0000-4000-8000-000000000031'
export const TRANSFORMATION_2 = 'f2000000-0000-4000-8000-000000000032'
export const UPSTREAM_ORDER = 'f2000000-0000-4000-8000-000000000041'
export const UPSTREAM_OUTPUT = 'f2000000-0000-4000-8000-000000000042'
export const UPSTREAM_TRANSFORMATION = 'f2000000-0000-4000-8000-000000000043'
export const UPSTREAM_PARENT_LABEL = 'f2000000-0000-4000-8000-000000000044'
export const DOWNSTREAM_ORDER = 'f2000000-0000-4000-8000-000000000051'
export const DOWNSTREAM_OUTPUT = 'f2000000-0000-4000-8000-000000000052'
export const DOWNSTREAM_LABEL = 'f2000000-0000-4000-8000-000000000053'
export const DOWNSTREAM_TRANSFORMATION = 'f2000000-0000-4000-8000-000000000054'

export function inputShare(inputLabelId: string, consumedKg: string, share: string | null) {
  return { inputLabelId, consumedKg, share }
}

export function lineageOutput(overrides: Record<string, unknown> = {}) {
  return {
    orderId: ORDER,
    outputId: OUTPUT_1,
    outputLabelId: OUTPUT_LABEL_1,
    genealogyTransformationId: TRANSFORMATION_1,
    shareStatus: 'calculated',
    shareCode: null,
    totalConsumedKg: '100',
    inputs: [inputShare(INPUT_LABEL_A, '75', '0.75'), inputShare(INPUT_LABEL_B, '25', '0.25')],
    ...overrides,
  }
}

export function secondOutput(overrides: Record<string, unknown> = {}) {
  return lineageOutput({
    outputId: OUTPUT_2,
    outputLabelId: OUTPUT_LABEL_2,
    genealogyTransformationId: TRANSFORMATION_2,
    totalConsumedKg: '40',
    inputs: [inputShare(INPUT_LABEL_A, '40', '1')],
    ...overrides,
  })
}

export function upstreamOutput(overrides: Record<string, unknown> = {}) {
  return lineageOutput({
    orderId: UPSTREAM_ORDER,
    outputId: UPSTREAM_OUTPUT,
    outputLabelId: INPUT_LABEL_A,
    genealogyTransformationId: UPSTREAM_TRANSFORMATION,
    totalConsumedKg: '500',
    inputs: [inputShare(UPSTREAM_PARENT_LABEL, '500', '1')],
    ...overrides,
  })
}

export function downstreamOutput(overrides: Record<string, unknown> = {}) {
  return lineageOutput({
    orderId: DOWNSTREAM_ORDER,
    outputId: DOWNSTREAM_OUTPUT,
    outputLabelId: DOWNSTREAM_LABEL,
    genealogyTransformationId: DOWNSTREAM_TRANSFORMATION,
    totalConsumedKg: '60',
    inputs: [inputShare(OUTPUT_LABEL_1, '60', '1')],
    ...overrides,
  })
}

type OutputBody = ReturnType<typeof lineageOutput>

/** The forward trace the server derives from `outputs`: each input label with every output it went into. */
export function forwardTrace(outputs: readonly OutputBody[]) {
  const byInput = new Map<string, { inputLabelId: string; outputs: unknown[] }>()
  for (const output of outputs) {
    for (const input of output.inputs) {
      const entry = byInput.get(input.inputLabelId) ?? { inputLabelId: input.inputLabelId, outputs: [] }
      entry.outputs.push({
        outputId: output.outputId,
        outputLabelId: output.outputLabelId,
        shareStatus: output.shareStatus,
        consumedKg: input.consumedKg,
        share: input.share,
      })
      byInput.set(input.inputLabelId, entry)
    }
  }
  return [...byInput.values()].sort((left, right) => left.inputLabelId.localeCompare(right.inputLabelId))
}

export function orderLineage(
  options: {
    readonly outputs?: readonly OutputBody[]
    readonly upstream?: readonly OutputBody[]
    readonly downstream?: readonly OutputBody[]
    readonly truncated?: boolean
  } = {},
) {
  const outputs = options.outputs ?? [lineageOutput(), secondOutput()]
  return {
    contractVersion: 1,
    orderId: ORDER,
    calculatorVersion: 1,
    truncated: options.truncated ?? false,
    outputs,
    inputs: forwardTrace(outputs),
    upstream: options.upstream ?? [upstreamOutput()],
    downstream: options.downstream ?? [downstreamOutput()],
  }
}
