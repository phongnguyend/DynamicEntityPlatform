import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderOpen, Pencil, Plus, Trash2 } from 'lucide-react'
import { Link } from 'react-router-dom'
import { api } from '../api'

export function DashboardsListPage({ tenantId }: { tenantId: string }) {
  const client = useQueryClient()
  const dashboards = useQuery({ queryKey: ['dashboards', tenantId], queryFn: () => api.dashboards(tenantId) })
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
    <div className="panel"><div className="panel-title"><h3>Dashboards</h3><div className="actions"><span>{dashboards.data?.length ?? 0} dashboard{dashboards.data?.length === 1 ? '' : 's'}</span><Link className="button" to="/dashboards/new"><Plus />New dashboard</Link></div></div>
      {dashboards.data?.length ? <ul className="definition-list">{dashboards.data.map(dashboard => <li key={dashboard.id}>
        <div><strong>{dashboard.name}</strong><small>{dashboard.definition.items.length} card{dashboard.definition.items.length === 1 ? '' : 's'}</small></div>
        <div className="actions"><Link className="link" to={`/dashboards/${dashboard.id}`}><FolderOpen />Open</Link><Link className="link" to={`/dashboards/${dashboard.id}/edit`}><Pencil />Edit</Link><button type="button" className="link danger" onClick={() => remove(dashboard.id, dashboard.name)}><Trash2 />Delete</button></div>
      </li>)}</ul> : <div className="dashboard-list-empty"><p className="empty">No dashboards yet.</p><Link className="button" to="/dashboards/new"><Plus />Create your first dashboard</Link></div>}
      {deleteDashboard.error && <p className="error">{deleteDashboard.error.message}</p>}
    </div>
  </section>
}
