import { postWorkCommand, type CreateTaskBody, type WorkCommandOutcome } from '../work/collaborationApi'
import { decodeWorkTask, type WorkTask } from '../work/collaborationContract'

export type CreationNeedOutcome = WorkCommandOutcome<WorkTask>
  | { readonly type: 'unknown'; readonly reason: 'authority'; readonly status: 401 | 403 }

function sameDeadline(a: string, b: string): boolean {
  // The form sends UTC; native Work stores DueAtUtc.ToUniversalTime(). Compare all native 100ns digits, without normalization by Date.parse.
  const utc = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/
  const left = utc.exec(a), right = utc.exec(b)
  return left !== null && right !== null && left[1] === right[1] && (left[2] ?? '').padEnd(7, '0') === (right[2] ?? '').padEnd(7, '0')
}

/** The native Work task wire is reused; this endpoint additionally checks the recipient's create authority. */
export async function createCounterpartyNeed(companyId: string, actorId: string, key: string, body: CreateTaskBody): Promise<CreationNeedOutcome> {
  const outcome = await postWorkCommand('/work/counterparty-creation-needs', companyId, key, body, 201, raw => {
    const task = decodeWorkTask(raw, companyId)
    return task !== null && task.assignerUserId.toLowerCase() === actorId.toLowerCase()
      && task.assigneeUserId.toLowerCase() === body.assigneeUserId.toLowerCase()
      && task.title === body.title && task.details === body.details
      && task.recordType === body.recordType && task.recordId === body.recordId
      && sameDeadline(task.dueAtUtc, body.dueAtUtc) ? task : null
  })
  if (outcome.type === 'applied' && !outcome.replayed && (outcome.data.status !== 'open' || outcome.data.version !== '1'))
    return { type: 'unknown', reason: 'unexpected' }
  // An authority refusal may follow task creation. It cannot authorize a new key.
  return outcome.type === 'refused' && (outcome.status === 401 || outcome.status === 403)
    ? { type: 'unknown', reason: 'authority', status: outcome.status } : outcome
}
