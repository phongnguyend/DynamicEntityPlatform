import { NavLink } from 'react-router-dom'
import { Bell, Calculator, Database, FileChartColumn, ListFilter, ListTree } from 'lucide-react'

export function EntityTabs({ entityId }: { entityId: string }) {
  return <nav className="tabs">
    <NavLink to={`/entities/${entityId}/records`} className={({ isActive }) => isActive ? 'active' : ''}><Database />Records</NavLink>
    <NavLink to={`/entities/${entityId}/views`} className={({ isActive }) => isActive ? 'active' : ''}><ListFilter />Views</NavLink>
    <NavLink to={`/entities/${entityId}/fields`} className={({ isActive }) => isActive ? 'active' : ''}><ListTree />Fields</NavLink>
    <NavLink to={`/entities/${entityId}/reports`} className={({ isActive }) => isActive ? 'active' : ''}><FileChartColumn />Reports</NavLink>
    <NavLink to={`/entities/${entityId}/metrics`} className={({ isActive }) => isActive ? 'active' : ''}><Calculator />Metrics</NavLink>
    <NavLink to={`/entities/${entityId}/alerts`} className={({ isActive }) => isActive ? 'active' : ''}><Bell />Alerts</NavLink>
  </nav>
}
