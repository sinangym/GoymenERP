import type { DelegationPerson } from './workDelegationApi'
import { WORK_MANAGEMENT_TEXT as T } from './workManagementText'

export const delegationPersonLabel = (people: readonly DelegationPerson[], id: string) =>
  people.find(person => person.userId === id)?.displayName ?? `${T.nameUnavailable} (${id})`
