import { useQuery } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api'
import { EntityTabs } from '../components/EntityTabs'
import { ReportBuilder } from '../components/ReportBuilder'
import { useEntityContext } from './useEntityContext'

export function ReportBuilderPage({ tenantId }: { tenantId: string }) {
  const { entityId = '', reportId } = useParams(); const navigate = useNavigate(); const context = useEntityContext(tenantId, entityId)
  const report = useQuery({ queryKey: ['report', tenantId, entityId, reportId], queryFn: () => api.report(tenantId, entityId, reportId!), enabled: Boolean(reportId) })
  if (context.isLoading || report.isLoading) return <p>Loading…</p>; if (context.error || report.error) return <p className="error">{(context.error ?? report.error)?.message}</p>
  return <section className="page"><header><div><p className="eyebrow">Report builder</p><h2>{report.data?.name ?? 'New report'}</h2></div></header><EntityTabs entityId={entityId} />
    <ReportBuilder tenantId={tenantId} entityId={entityId} fields={context.fields} report={report.data} onSaved={saved => navigate(`/entities/${entityId}/reports/${saved.id}`)} /></section>
}
