import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderOpen, Pencil, Plus, Trash2 } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api'
import { DashboardEditor } from '../components/DashboardEditor'
import type { Dashboard } from '../types'

export function DashboardsListPage({ tenantId }: { tenantId: string }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const dashboards = useQuery({ queryKey: ['dashboards', tenantId], queryFn: () => api.dashboards(tenantId) })
  const [editing, setEditing] = useState<Dashboard | null | undefined>(undefined)
  const deleteDashboard = useMutation({
    mutationFn: (dashboardId: string) => api.deleteDashboard(tenantId, dashboardId),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['dashboards', tenantId] }),
  })

  function remove(id: string, name: string) {
    if (confirm(`Delete the dashboard "${name}"?`)) deleteDashboard.mutate(id)
  }

  if (dashboards.isLoading) return <p>Loading dashboards…</p>
  if (dashboards.error) return <p className="error">{dashboards.error.message}</p>

  return <section className="page"><header><div><p className="eyebrow">Workspace overview</p><h2>Dashboards</h2></div></header>
    <div className="panel"><div className="panel-header"><h3>Dashboards</h3><div className="actions"><span>{dashboards.data?.length ?? 0} dashboard{dashboards.data?.length === 1 ? '' : 's'}</span><button type="button" onClick={() => setEditing(null)}><Plus />New dashboard</button></div></div>
      <div className="panel-body">{dashboards.data?.length ? <ul className="definition-list">{dashboards.data.map(dashboard => <li key={dashboard.id}>
        <div><strong>{dashboard.name}</strong><small>{dashboard.definition.items.length} card{dashboard.definition.items.length === 1 ? '' : 's'}</small></div>
        <div className="actions"><Link className="link" to={`/dashboards/${dashboard.id}`}><FolderOpen />Open</Link><button type="button" className="link" onClick={() => setEditing(dashboard)}><Pencil />Edit</button><button type="button" className="link danger" onClick={() => remove(dashboard.id, dashboard.name)}><Trash2 />Delete</button></div>
      </li>)}</ul> : <div className="dashboard-list-empty"><p className="empty">No dashboards yet.</p><button type="button" onClick={() => setEditing(null)}><Plus />Create your first dashboard</button></div>}
      {deleteDashboard.error && <p className="error">{deleteDashboard.error.message}</p>}</div>
    </div>
    {editing !== undefined && <DashboardEditor tenantId={tenantId} dashboard={editing} onClose={() => setEditing(undefined)} onSaved={saved => {
      const created = !editing
      setEditing(undefined)
      client.setQueryData<Dashboard>(['dashboard', tenantId, saved.id], saved)
      void client.invalidateQueries({ queryKey: ['dashboards', tenantId] })
      if (created) navigate(`/dashboards/${saved.id}`)
    }} />}
  </section>
}
