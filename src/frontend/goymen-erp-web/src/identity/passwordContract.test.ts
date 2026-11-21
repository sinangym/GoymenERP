import { describe, expect, it } from 'vitest'
import { classifyPasswordChange } from './passwordContract'

const problem = (status: number, code: string) => ({ type: 'about:blank', title: 'SENTETIK', status, detail: 'SENTETIK', code, correlationId: null })

describe('ID-2F password contract', () => {
  it('accepts only the documented success status and current refusal codes', () => {
    expect(classifyPasswordChange(204, null)).toEqual({ kind: 'changed' })
    expect(classifyPasswordChange(200, {})).toEqual({ kind: 'unknown', reason: 'malformed' })
    expect(classifyPasswordChange(400, problem(400, 'current_password_invalid'))).toEqual({ kind: 'refused', code: 'current_password_invalid' })
    expect(classifyPasswordChange(400, { ...problem(400, 'password_policy_violation'), field: 'newPassword', violations: ['minimum_length'] })).toEqual({ kind: 'refused', code: 'password_policy_violation' })
    expect(classifyPasswordChange(409, { ...problem(409, 'version_conflict'), expectedVersion: '1', currentVersion: '2' })).toEqual({ kind: 'conflict' })
  })

  it('fails closed on missing extra mistyped and unknown problem fields', () => {
    const { detail: _missing, ...missing } = problem(400, 'current_password_invalid')
    for (const body of [missing, { ...problem(400, 'current_password_invalid'), extra: true },
      { ...problem(400, 'current_password_invalid'), field: 1 }, { ...problem(400, 'password_policy_violation'), violations: [false] },
      problem(400, 'unknown'), problem(409, 'current_password_invalid'), null]) {
      expect(classifyPasswordChange(400, body)).toEqual({ kind: 'unknown', reason: 'malformed' })
    }
  })
})
