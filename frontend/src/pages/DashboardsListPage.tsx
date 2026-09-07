import { useState } from 'react'
import { FolderOpen, Pencil, Plus, Trash2 } from 'lucide-react'
import { Link } from 'react-router-dom'
import { deleteDashboard, loadDashboards } from './dashboardStorage'

export function DashboardsListPage({ tenantId }: { tenantId: string }) {
  const [collection, setCollection] = useState(() => loadDashboards(tenantId))

  function remove(id: string, name: string) {
    if (confirm(`Delete the dashboard "${name}"?`)) setCollection(deleteDashboard(tenantId, id))
  }

  return <section className="page"><header><div><p className="eyebrow">Workspace overview</p><h2>Dashboards</h2></div></header>
    <div className="panel"><div className="panel-title"><h3>Dashboards</h3><div className="actions"><span>{collection.dashboards.length} dashboard{collection.dashboards.length === 1 ? '' : 's'}</span><Link className="button" to="/dashboards/new"><Plus />New dashboard</Link></div></div>
      {collection.dashboards.length ? <ul className="definition-list">{collection.dashboards.map(dashboard => <li key={dashboard.id}>
        <div><strong>{dashboard.name}</strong><small>{dashboard.items.length} card{dashboard.items.length === 1 ? '' : 's'}</small></div>
        <div className="actions"><Link className="link" to={`/dashboards/${dashboard.id}`}><FolderOpen />Open</Link><Link className="link" to={`/dashboards/${dashboard.id}/edit`}><Pencil />Edit</Link><button type="button" className="link danger" onClick={() => remove(dashboard.id, dashboard.name)}><Trash2 />Delete</button></div>
      </li>)}</ul> : <div className="dashboard-list-empty"><p className="empty">No dashboards yet.</p><Link className="button" to="/dashboards/new"><Plus />Create your first dashboard</Link></div>}
    </div>
  </section>
}
