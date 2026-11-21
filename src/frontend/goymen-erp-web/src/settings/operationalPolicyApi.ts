import { readJson, send, noBody, type CommandContext } from './discountLimitApi'
import { decodeOperationalPolicy, operationalPolicyPath, type OperationalPolicyKind, type RecordOperationalPolicyBody } from './operationalPolicyContract'
export const fetchOperationalPolicy = (kind: OperationalPolicyKind, company: string, signal?: AbortSignal) => readJson(operationalPolicyPath(kind), company, (raw) => decodeOperationalPolicy(raw, company, kind), signal)
export const recordOperationalPolicy = (kind: OperationalPolicyKind, context: CommandContext, body: RecordOperationalPolicyBody) => send('POST', `${operationalPolicyPath(kind)}/versions`, context, body, 204, noBody)
