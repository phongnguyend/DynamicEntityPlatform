import type { ResponsiveLayouts } from 'react-grid-layout'

export type DashboardItem = { kind: 'report' | 'metric'; entityId: string; id: string }
export type GridBreakpoint = 'lg' | 'md' | 'sm' | 'xs' | 'xxs'
export type DashboardState = { items: DashboardItem[]; layouts: ResponsiveLayouts<GridBreakpoint> }
export type SavedDashboard = DashboardState & { id: string; name: string }
export type DashboardCollection = { activeDashboardId: string; dashboards: SavedDashboard[] }

const storageKey = (tenantId: string) => `dynamic-data.dashboard.${tenantId}`

export const itemKey = (item: DashboardItem) => `${item.kind}:${item.entityId}:${item.id}`
export const newDashboardId = () => typeof crypto !== 'undefined' && crypto.randomUUID
  ? crypto.randomUUID()
  : `dashboard-${Date.now()}-${Math.random().toString(36).slice(2)}`
export const emptyDashboard = (name: string): SavedDashboard => ({ id: newDashboardId(), name, items: [], layouts: {} })

const isDashboardItem = (item: unknown): item is DashboardItem => Boolean(item && typeof item === 'object' &&
  ((item as DashboardItem).kind === 'report' || (item as DashboardItem).kind === 'metric') &&
  typeof (item as DashboardItem).entityId === 'string' && typeof (item as DashboardItem).id === 'string')

function parseDashboardState(value: unknown): DashboardState {
  if (Array.isArray(value)) return { items: value.filter(isDashboardItem), layouts: {} }
  if (!value || typeof value !== 'object') return { items: [], layouts: {} }
  const saved = value as Partial<DashboardState>
  return { items: Array.isArray(saved.items) ? saved.items.filter(isDashboardItem) : [], layouts: saved.layouts ?? {} }
}

export function saveDashboards(tenantId: string, collection: DashboardCollection) {
  localStorage.setItem(storageKey(tenantId), JSON.stringify(collection))
}

export function loadDashboards(tenantId: string): DashboardCollection {
  try {
    const raw = localStorage.getItem(storageKey(tenantId))
    const value = JSON.parse(raw ?? '[]') as unknown
    if (value && typeof value === 'object' && Array.isArray((value as Partial<DashboardCollection>).dashboards)) {
      const saved = value as Partial<DashboardCollection>
      const dashboards = saved.dashboards!.flatMap((dashboard, index) => {
        if (!dashboard || typeof dashboard !== 'object') return []
        const candidate = dashboard as Partial<SavedDashboard>
        return [{
          ...parseDashboardState(candidate),
          id: typeof candidate.id === 'string' && candidate.id ? candidate.id : newDashboardId(),
          name: typeof candidate.name === 'string' && candidate.name.trim() ? candidate.name.trim() : `Dashboard ${index + 1}`,
        }]
      })
      const collection = {
        dashboards,
        activeDashboardId: dashboards.some(dashboard => dashboard.id === saved.activeDashboardId) ? saved.activeDashboardId! : dashboards[0]?.id ?? '',
      }
      saveDashboards(tenantId, collection)
      return collection
    }
    const dashboard = { ...parseDashboardState(value), id: newDashboardId(), name: 'Dashboard 1' }
    const collection = { activeDashboardId: dashboard.id, dashboards: [dashboard] }
    saveDashboards(tenantId, collection)
    return collection
  } catch {
    return { activeDashboardId: '', dashboards: [] }
  }
}

export function deleteDashboard(tenantId: string, dashboardId: string) {
  const current = loadDashboards(tenantId)
  const dashboards = current.dashboards.filter(dashboard => dashboard.id !== dashboardId)
  const collection = {
    dashboards,
    activeDashboardId: current.activeDashboardId === dashboardId ? dashboards[0]?.id ?? '' : current.activeDashboardId,
  }
  saveDashboards(tenantId, collection)
  return collection
}
