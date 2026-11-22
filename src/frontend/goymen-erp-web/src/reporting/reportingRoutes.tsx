/**
 * WP14 report centre routes. They add no capability of their own: the server lists only the reports whose owner read
 * permissions the actor holds now, and re-authorizes every run, export and package. The auditor route is declared before
 * the report route so it is never read as a report id.
 */
import type { RouteObject } from 'react-router'
import { AuditorPackagePage } from './AuditorPackagePage'
import { ReportCenterPage } from './ReportCenterPage'
import { ReportRunPage } from './ReportRunPage'
import { REPORTING_ROUTE } from './reportingContract'

export const reportingRoutes: RouteObject[] = [
  { path: REPORTING_ROUTE.center.slice(1), element: <ReportCenterPage /> },
  { path: REPORTING_ROUTE.auditor.slice(1), element: <AuditorPackagePage /> },
  { path: REPORTING_ROUTE.report.slice(1), element: <ReportRunPage /> },
]
