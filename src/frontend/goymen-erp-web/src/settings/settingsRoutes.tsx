/**
 * The Settings route table, spread into `shell/router.tsx` (before the catch-all). Each entry is wrapped in
 * `CapabilityRoute`: an ungranted capability mounts nothing and renders the same neutral not-found block any unknown
 * path gets, and calls nothing.
 */

import type { RouteObject } from 'react-router'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { SETTINGS_CAPABILITY, SETTINGS_ROUTE } from './capabilities'
import { ExchangeRatesPage } from './ExchangeRatesPage'
import { LoginRateLimitPage } from './LoginRateLimitPage'
import { ReferenceListPage } from './ReferenceListPage'
import { SalesDiscountLimitsPage } from './SalesDiscountLimitsPage'
import { SalesPolicyApprovalThresholdsPage } from './SalesPolicyApprovalThresholdsPage'
import { SalesPolicyDeliveryTolerancePage } from './SalesPolicyDeliveryTolerancePage'
import { TaxMasterPage } from './TaxMasterPage'
import { UnitConversionPolicyPage } from './UnitConversionPolicyPage'
import { UnitProfilePage } from './UnitProfilePage'
import { MoneyProfilePage } from './MoneyProfilePage'
import { WorkCenterCapacityPage } from './WorkCenterCapacityPage'

export const settingsRoutes: RouteObject[] = [
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.moneyProfiles].slice(1),
    element: <CapabilityRoute capability={SETTINGS_CAPABILITY.moneyProfiles}><MoneyProfilePage /></CapabilityRoute>,
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesDiscountLimits].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesDiscountLimits}>
        <SalesDiscountLimitsPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.taxMaster].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.taxMaster}>
        <TaxMasterPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.exchangeRates].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.exchangeRates}>
        <ExchangeRatesPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesDeliveryTolerance].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesDeliveryTolerance}>
        <SalesPolicyDeliveryTolerancePage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.salesApprovalThresholds].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.salesApprovalThresholds}>
        <SalesPolicyApprovalThresholdsPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.loginRateLimit].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.loginRateLimit}>
        <LoginRateLimitPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.unitConversionPolicy].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.unitConversionPolicy}>
        <UnitConversionPolicyPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.unitProfiles].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.unitProfiles}>
        <UnitProfilePage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.workCenterCapacity].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.workCenterCapacity}>
        <WorkCenterCapacityPage />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.purchasingReferenceLists].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.purchasingReferenceLists}>
        <ReferenceListPage group="purchasing" />
      </CapabilityRoute>
    ),
  },
  {
    path: SETTINGS_ROUTE[SETTINGS_CAPABILITY.productionReferenceLists].slice(1),
    element: (
      <CapabilityRoute capability={SETTINGS_CAPABILITY.productionReferenceLists}>
        <ReferenceListPage group="production" />
      </CapabilityRoute>
    ),
  },
]
