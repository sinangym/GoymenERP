/**
 * URT-S22 lineage decoder: strict keys, lossless text kg and shares, the order asked for, shares only on a calculated
 * lineage, and a forward trace that is exactly the backward trace read the other way round.
 */

import { describe, expect, it } from 'vitest'
import { decodeOrderLineage } from './lineageContract'
import {
  INPUT_LABEL_A,
  INPUT_LABEL_B,
  OUTPUT_1,
  OUTPUT_2,
  UPSTREAM_ORDER,
  forwardTrace,
  inputShare,
  lineageOutput,
  orderLineage,
  secondOutput,
  upstreamOutput,
} from './lineageFixture'
import { ORDER } from './testHarness'

function withoutTruncated(): Record<string, unknown> {
  const body: Record<string, unknown> = { ...orderLineage() }
  delete body.truncated
  return body
}

describe('decodeOrderLineage', () => {
  it('decodes the server shape and keeps every kg and share as the server text', () => {
    const lineage = decodeOrderLineage(orderLineage(), ORDER)

    expect(lineage).not.toBeNull()
    expect(lineage?.truncated).toBe(false)
    expect(lineage?.outputs.map((output) => output.outputId)).toEqual([OUTPUT_1, OUTPUT_2])
    expect(lineage?.outputs[0].inputs).toEqual([
      { inputLabelId: INPUT_LABEL_A, consumedKg: '75', share: '0.75' },
      { inputLabelId: INPUT_LABEL_B, consumedKg: '25', share: '0.25' },
    ])
    expect(lineage?.inputs.map((input) => [input.inputLabelId, input.outputs.map((output) => output.outputId)])).toEqual([
      [INPUT_LABEL_A, [OUTPUT_1, OUTPUT_2]],
      [INPUT_LABEL_B, [OUTPUT_1]],
    ])
    expect(lineage?.upstream.map((output) => output.orderId)).toEqual([UPSTREAM_ORDER])
  })

  it('keeps an unrounded share exactly as it came', () => {
    const third = '0.3333333333333333333333333333'
    const output = lineageOutput({
      totalConsumedKg: '3',
      inputs: [inputShare(INPUT_LABEL_A, '1', third), inputShare(INPUT_LABEL_B, '2', '0.6666666666666666666666666667')],
    })
    const lineage = decodeOrderLineage(orderLineage({ outputs: [output], upstream: [], downstream: [] }), ORDER)

    expect(lineage?.outputs[0].inputs[0].share).toBe(third)
  })

  it('accepts a pending or unreadable lineage with its code and no share, and an empty lineage', () => {
    const pending = lineageOutput({
      shareStatus: 'unreadable',
      shareCode: 'lineage_input_without_consumption',
      totalConsumedKg: null,
      inputs: [inputShare(INPUT_LABEL_A, '75', null), inputShare(INPUT_LABEL_B, '25', null)],
    })
    const lineage = decodeOrderLineage(orderLineage({ outputs: [pending], upstream: [], downstream: [] }), ORDER)
    expect(lineage?.outputs[0].shareCode).toBe('lineage_input_without_consumption')
    expect(lineage?.inputs[0].outputs[0].share).toBeNull()

    const empty = decodeOrderLineage(orderLineage({ outputs: [], upstream: [], downstream: [] }), ORDER)
    expect(empty?.outputs).toEqual([])
    expect(empty?.inputs).toEqual([])
  })

  const base = orderLineage()
  it.each<[string, unknown]>([
    ['not an object', null],
    ['an extra key', { ...base, extra: 1 }],
    ['a missing key', withoutTruncated()],
    ['another contract version', { ...base, contractVersion: 2 }],
    ['another calculator version', { ...base, calculatorVersion: 2 }],
    ['another order', { ...base, orderId: UPSTREAM_ORDER }],
    ['a truncated flag that is not a boolean', { ...base, truncated: 'false' }],
    ['a kg as a JSON number', orderLineage({ outputs: [lineageOutput({ totalConsumedKg: 100 })] })],
    ['a kg with a trailing zero', orderLineage({ outputs: [lineageOutput({ totalConsumedKg: '100.0' })] })],
    ['a share above one', orderLineage({ outputs: [lineageOutput({ inputs: [inputShare(INPUT_LABEL_A, '75', '1.5')] })] })],
    ['an unknown share status', orderLineage({ outputs: [lineageOutput({ shareStatus: 'estimated' })] })],
    [
      'a calculated line without its share',
      orderLineage({ outputs: [lineageOutput({ inputs: [inputShare(INPUT_LABEL_A, '75', null)] })] }),
    ],
    ['a calculated lineage without its total', orderLineage({ outputs: [lineageOutput({ totalConsumedKg: null })] })],
    ['a calculated lineage with a code', orderLineage({ outputs: [lineageOutput({ shareCode: 'lineage_no_input' })] })],
    [
      'a pending lineage with a share',
      orderLineage({
        outputs: [lineageOutput({ shareStatus: 'pending', shareCode: 'lineage_no_input', totalConsumedKg: null })],
      }),
    ],
    [
      'a pending lineage without its code',
      orderLineage({
        outputs: [
          lineageOutput({ shareStatus: 'pending', totalConsumedKg: null, inputs: [inputShare(INPUT_LABEL_A, '1', null)] }),
        ],
      }),
    ],
    [
      'a repeated input label',
      orderLineage({
        outputs: [lineageOutput({ inputs: [inputShare(INPUT_LABEL_A, '50', '0.5'), inputShare(INPUT_LABEL_A, '50', '0.5')] })],
      }),
    ],
    ['a repeated output', orderLineage({ outputs: [lineageOutput(), lineageOutput()] })],
    ['an own output of another order', orderLineage({ outputs: [lineageOutput({ orderId: UPSTREAM_ORDER })] })],
    ['an upstream output of this order', orderLineage({ upstream: [upstreamOutput({ orderId: ORDER })] })],
    ['a nil id', orderLineage({ outputs: [lineageOutput({ outputLabelId: '00000000-0000-0000-0000-000000000000' })] })],
    ['a forward trace missing an input', { ...base, inputs: base.inputs.slice(1) }],
    ['a forward trace with another share', { ...base, inputs: forwardTrace([lineageOutput(), secondOutput({ inputs: [inputShare(INPUT_LABEL_A, '40', '0.5')] })]) }],
    [
      'a forward trace naming an output the order does not have',
      { ...base, inputs: forwardTrace([lineageOutput(), secondOutput(), upstreamOutput({ orderId: ORDER })]) },
    ],
  ])('refuses %s', (_case, value) => {
    expect(decodeOrderLineage(value, ORDER)).toBeNull()
  })
})
