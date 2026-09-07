import { useState, type FormEvent } from 'react'
import { Save, X } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { emptyDashboard, loadDashboards, saveDashboards } from './dashboardStorage'

export function DashboardFormPage({ tenantId }: { tenantId: string }) {
  const { dashboardId } = useParams()
  const navigate = useNavigate()
  const [collection] = useState(() => loadDashboards(tenantId))
  const dashboard = dashboardId ? collection.dashboards.find(value => value.id === dashboardId) : undefined
  const editing = Boolean(dashboardId)
  const [name, setName] = useState(dashboard?.name ?? '')
  const trimmedName = name.trim()
  const duplicate = collection.dashboards.some(value => value.id !== dashboardId && value.name.toLocaleLowerCase() === trimmedName.toLocaleLowerCase())

  if (editing && !dashboard) return <section className="page"><p className="error">Dashboard not found.</p><Link className="button" to="/dashboards">Back to dashboards</Link></section>

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!trimmedName || duplicate) return
    if (dashboard) {
      saveDashboards(tenantId, { ...collection, dashboards: collection.dashboards.map(value => value.id === dashboard.id ? { ...value, name: trimmedName } : value) })
      navigate(`/dashboards/${dashboard.id}`)
    } else {
      const created = emptyDashboard(trimmedName)
      saveDashboards(tenantId, { activeDashboardId: created.id, dashboards: [...collection.dashboards, created] })
      navigate(`/dashboards/${created.id}`)
    }
  }

  return <section className="page form-page"><header><div><p className="eyebrow">{editing ? 'Edit dashboard' : 'New dashboard'}</p><h2>{editing ? dashboard!.name : 'Create dashboard'}</h2></div></header>
    <div className="panel"><form className="dynamic-form" onSubmit={submit}>
      <label className="field">Name<input autoFocus required maxLength={80} value={name} onChange={event => setName(event.target.value)} placeholder="e.g. Executive overview" /></label>
      {duplicate && <p className="error">A dashboard with this name already exists.</p>}
      <div className="actions"><button type="submit" disabled={!trimmedName || duplicate}><Save />{editing ? 'Save changes' : 'Create dashboard'}</button><Link className="button secondary" to={dashboard ? `/dashboards/${dashboard.id}` : '/dashboards'}><X />Cancel</Link></div>
    </form></div>
  </section>
}
