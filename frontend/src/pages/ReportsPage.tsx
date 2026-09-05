import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { EntityTabs } from '../components/EntityTabs'
import { useEntityContext } from './useEntityContext'

export function ReportsPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams(); const context = useEntityContext(tenantId, entityId); const client = useQueryClient()
  const reports = useQuery({ queryKey: ['reports', tenantId, entityId], queryFn: () => api.reports(tenantId, entityId) })
  const remove = useMutation({ mutationFn: api.deleteReport.bind(null, tenantId, entityId), onSuccess: () => client.invalidateQueries({ queryKey: ['reports', tenantId, entityId] }) })
  async function exportCsv(reportId: string) { const blob = await api.exportReport(tenantId, entityId, reportId); const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = `report-${reportId}.csv`; anchor.click(); URL.revokeObjectURL(url) }
  if (context.isLoading) return <p>Loading…</p>; if (context.error || reports.error) return <p className="error">{(context.error ?? reports.error)?.message}</p>
  return <section className="page"><header><div><p className="eyebrow">Reports</p><h2>{context.entity?.displayName}</h2></div><Link className="button" to={`/entities/${entityId}/reports/new`}>New report</Link></header><EntityTabs entityId={entityId} />
    <div className="panel"><div className="panel-title"><h3>Saved reports</h3><span>{reports.data?.length ?? 0}</span></div>{reports.data?.length ? <ul className="definition-list">{reports.data.map(report => <li key={report.id}><div><strong>{report.name}</strong><small>{report.description || `${report.visualization} visualization`}</small></div><div className="actions"><Link className="link" to={`/entities/${entityId}/reports/${report.id}`}>Open</Link><button className="link" onClick={() => exportCsv(report.id)}>CSV</button><button className="link danger" onClick={() => remove.mutate(report.id)}>Delete</button></div></li>)}</ul> : <p className="empty">No reports yet.</p>}</div>
  </section>
}
