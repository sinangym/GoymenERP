import { readJson } from '../settings/discountLimitApi'
import { NOTIFICATIONS_PATH, decodeWorkNotifications } from './notificationContract'
export const fetchWorkNotifications = (companyId: string, includePassive: boolean, signal?: AbortSignal, beforeId: string | null = null) => readJson(`${NOTIFICATIONS_PATH}?includePassive=${includePassive}${beforeId === null ? '' : `&beforeId=${encodeURIComponent(beforeId)}`}`, companyId, (raw) => decodeWorkNotifications(raw, companyId), signal)
