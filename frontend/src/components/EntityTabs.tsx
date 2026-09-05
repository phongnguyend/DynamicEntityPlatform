import { NavLink } from 'react-router-dom'

export function EntityTabs({ entityId }: { entityId: string }) {
  return <nav className="tabs">
    <NavLink to={`/entities/${entityId}/records`} className={({ isActive }) => isActive ? 'active' : ''}>Records</NavLink>
    <NavLink to={`/entities/${entityId}/fields`} className={({ isActive }) => isActive ? 'active' : ''}>Fields</NavLink>
  </nav>
}
