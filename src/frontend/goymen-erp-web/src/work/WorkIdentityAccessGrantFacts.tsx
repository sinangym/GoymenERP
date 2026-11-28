import type {WorkIdentityAccessGrantPreview} from './workIdentityAccessGrantDecision'
import {IDENTITY_ACCESS_GRANT_WORK_TEXT as T} from './workIdentityAccessGrantText'
import styles from './Work.module.css'
export function WorkIdentityAccessGrantFacts({human}:{readonly human:WorkIdentityAccessGrantPreview}) {
 const r=human.request,f=T.fields
 const facts=[[f.requestId,r.requestId],[f.requester,r.requestedBy.displayName],[f.requesterId,r.requestedBy.userId],[f.target,r.target.displayName],[f.targetId,r.target.userId],
  [f.roleId,r.roleId],[f.roleCode,r.roleCode],[f.roleName,r.roleName],[f.status,T.status[r.status]],[f.requestedAtUtc,r.requestedAtUtc],
  [f.validFromUtc,r.validFromUtc],[f.validUntilUtc,r.validUntilUtc],[f.reason,r.reason],[f.version,r.version],
  [f.requiresSecondApproval,human.requiresSecondApproval?T.yes:T.no],[f.targetActive,human.targetActive?T.yes:T.no],[f.roleActive,human.roleActive?T.yes:T.no],
  [f.currentRoleIsCritical,human.currentRoleIsCritical?T.yes:T.no]]
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><dl className={styles.facts}>
  {facts.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value??T.none}</dd></div>)}
 </dl><h4>{T.permissions}</h4>{human.currentPermissions.length===0?<p>{T.emptyPermissions}</p>:<ul>{human.currentPermissions.map(code=><li key={code}>{code}</li>)}</ul>}</section>
}
