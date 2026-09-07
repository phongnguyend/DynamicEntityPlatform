import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Save, X } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import type { Dashboard } from '../types'

export function DashboardFormPage({ tenantId }: { tenantId: string }) {
  const { dashboardId } = useParams()
  const dashboards = useQuery({ queryKey: ['dashboards', tenantId], queryFn: () => api.dashboards(tenantId) })

  if (dashboards.isLoading) return <p>Loading dashboards…</p>
  if (dashboards.error) return <p className="error">{dashboards.error.message}</p>
  const dashboard = dashboardId ? dashboards.data?.find(value => value.id === dashboardId) : undefined
  if (dashboardId && !dashboard) return <section className="page"><p className="error">Dashboard not found.</p><Link className="button" to="/dashboards">Back to dashboards</Link></section>

  return <DashboardForm tenantId={tenantId} dashboard={dashboard} dashboards={dashboards.data ?? []} />
}

function DashboardForm({ tenantId, dashboard, dashboards }: { tenantId: string; dashboard?: Dashboard; dashboards: Dashboard[] }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const [name, setName] = useState(dashboard?.name ?? '')
  const trimmedName = name.trim()
  const duplicate = dashboards.some(value => value.id !== dashboard?.id && value.name.toLocaleLowerCase() === trimmedName.toLocaleLowerCase())
  const save = useMutation({
    mutationFn: () => dashboard
      ? api.updateDashboard(tenantId, dashboard.id, trimmedName, dashboard.definition)
      : api.createDashboard(tenantId, trimmedName, { items: [], layouts: {} }),
    onSuccess: saved => {
      client.setQueryData<Dashboard>(['dashboard', tenantId, saved.id], saved)
      void client.invalidateQueries({ queryKey: ['dashboards', tenantId] })
      navigate(`/dashboards/${saved.id}`)
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (trimmedName && !duplicate) save.mutate()
  }

  return <section className="page form-page"><header><div><p className="eyebrow">{dashboard ? 'Edit dashboard' : 'New dashboard'}</p><h2>{dashboard?.name ?? 'Create dashboard'}</h2></div></header>
    <div className="panel"><form className="panel-body dynamic-form" onSubmit={submit}>
      <label className="field">Name<input autoFocus required maxLength={80} value={name} onChange={event => setName(event.target.value)} placeholder="e.g. Executive overview" /></label>
      {duplicate && <p className="error">A dashboard with this name already exists.</p>}
      {save.error && <p className="error">{save.error.message}</p>}
      <div className="panel-footer"><button type="submit" disabled={!trimmedName || duplicate || save.isPending}><Save />{save.isPending ? 'Saving…' : dashboard ? 'Save changes' : 'Create dashboard'}</button><Link className="button secondary" to={dashboard ? `/dashboards/${dashboard.id}` : '/dashboards'}><X />Cancel</Link></div>
    </form></div>
  </section>
}
