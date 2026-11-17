import type { RouteObject } from 'react-router'
import { SubcontractDeliveryListPage, SubcontractDeliveryCreatePage, SubcontractDeliveryDetailPage } from '../logistics/SubcontractDeliveryPages'
import { CapabilityRoute } from './CapabilityRoute'
import { CAPABILITY, CAPABILITY_ROUTE } from './capabilities'

export const logisticsSubcontractRoutes: RouteObject[] = [
  { path: CAPABILITY_ROUTE[CAPABILITY.logisticsSubcontractList].slice(1),
    element: <CapabilityRoute capability={CAPABILITY.logisticsSubcontractList}><SubcontractDeliveryListPage /></CapabilityRoute> },
  { path: CAPABILITY_ROUTE[CAPABILITY.logisticsSubcontractCreate].slice(1),
    element: <CapabilityRoute capability={CAPABILITY.logisticsSubcontractCreate}><SubcontractDeliveryCreatePage /></CapabilityRoute> },
  { path: CAPABILITY_ROUTE[CAPABILITY.logisticsSubcontractDetail].slice(1),
    element: <CapabilityRoute capability={CAPABILITY.logisticsSubcontractDetail}><SubcontractDeliveryDetailPage /></CapabilityRoute> },
]
