/** WP2 transport: the shared `readJson` (company in `X-Company-Id`, 200 + decode or the classified outcome). Read only. */

import { readJson } from '../settings/discountLimitApi'
import type { ViewOutcome } from '../shell/uiState'
import { APPROVAL_FOLLOW_UP_PATH, decodeApprovalFollowUpPage, type ApprovalFollowUpPage, type ApprovalFollowUpView } from './approvalFollowUpContract'

export const fetchApprovalFollowUps = (companyId: string, view: ApprovalFollowUpView, signal?: AbortSignal): Promise<ViewOutcome<ApprovalFollowUpPage>> =>
  readJson(`${APPROVAL_FOLLOW_UP_PATH}?view=${view}`, companyId, (raw) => decodeApprovalFollowUpPage(raw, companyId, view), signal)
