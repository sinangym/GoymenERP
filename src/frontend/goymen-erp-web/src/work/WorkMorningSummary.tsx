import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed,contextKey,contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { fetchMorningSummary } from './workMorningSummaryApi'
import { MORNING_SUMMARY_TEXT as T,MORNING_KIND_TEXT } from './workMorningSummaryText'
import styles from './Work.module.css'
export function WorkMorningSummary(){
 const shell=useShellState(),context=contextKey(shell)
 if(context===null || capabilityFailClosed(shell) || shell.activeContext?.scopeMode!=='single')return null
 const namespace=contextKeyString(context),company=shell.activeContext.companyId!
 return <Summary key={`${namespace}:${shell.routeEntryGeneration}`} namespace={namespace} generation={shell.routeEntryGeneration} company={company}/>
}
function Summary({namespace,generation,company}:{readonly namespace:string;readonly generation:number;readonly company:string}){
 const boundary=useAuthBoundary()
 const query=useQuery({queryKey:[namespace,generation,'work-morning-summary'],retry:false,staleTime:0,gcTime:0,queryFn:async({signal})=>{
  const captured=boundary.capture(),result=await fetchMorningSummary(company,signal)
  if(result.type==='http')boundary.report(result.status,captured)
  return result
 }})
 const result=query.isFetching || !query.isFetchedAfterMount?undefined:query.data
 const value=result?.type==='ok-single'?result.data:null
 return <section className={styles.section} aria-label={T.heading}><h2>{T.heading}</h2><p>{T.scope}</p>
  {result===undefined?<StateBlock state={{kind:'initial-loading'}} subject={T.heading}/>:result.type!=='ok-single'?<>
    {result.type==='http' && result.status===503?<p role="status">{T.unavailable}</p>:null}<StateBlock state={classifyOutcome(result)} subject={T.heading}/>
   </>:value===null?<p>{T.empty}</p>:<><dl className={styles.facts}><dt>{T.slot}</dt><dd><time dateTime={value.slotUtc}>{value.slotUtc}</time></dd>
    <dt>{T.schedule}</dt><dd>{value.scheduleVersion}</dd><dt>{T.digest}</dt><dd>{value.digestId}</dd></dl>
    {value.counts.length===0?<p>{T.emptyCounts}</p>:<table><thead><tr><th scope="col">{T.kind}</th><th scope="col">{T.count}</th></tr></thead><tbody>{value.counts.map(row=><tr key={row.kind}><th scope="row">{MORNING_KIND_TEXT[row.kind]}</th><td>{row.count}</td></tr>)}</tbody></table>}
   </>}
  <Button type="button" disabled={query.isFetching} onClick={()=>void query.refetch()}>{T.retry}</Button>
 </section>
}
