import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../../shell/CapabilityRoute'
import type { CapabilityId } from '../../shell/capabilities'
import { PAINT_MEMBERSHIP_CAPABILITY, PAINT_MEMBERSHIP_ROUTE } from './contract'
import { PaintMembershipPage } from './PaintMembershipPage'

/** Same fail-closed shared entry gate as the other Product mounts; this declaration grants no capability. */
export const paintMembershipRoutes: RouteObject[] = [{
  path: PAINT_MEMBERSHIP_ROUTE.slice(1),
  element: <CapabilityRoute capability={PAINT_MEMBERSHIP_CAPABILITY as CapabilityId}><PaintMembershipPage /></CapabilityRoute>,
}]
