import { NavLink } from 'react-router-dom'

export function EntityTabs({ entityId }: { entityId: string }) {
  return <nav className="tabs">
    <NavLink to={`/entities/${entityId}/records`} className={({ isActive }) => isActive ? 'active' : ''}>Records</NavLink>
    <NavLink to={`/entities/${entityId}/fields`} className={({ isActive }) => isActive ? 'active' : ''}>Fields</NavLink>
    <NavLink to={`/entities/${entityId}/reports`} className={({ isActive }) => isActive ? 'active' : ''}>Reports</NavLink>
    <NavLink to={`/entities/${entityId}/metrics`} className={({ isActive }) => isActive ? 'active' : ''}>Metrics</NavLink>
    <NavLink to={`/entities/${entityId}/alerts`} className={({ isActive }) => isActive ? 'active' : ''}>Alerts</NavLink>
  </nav>
}
