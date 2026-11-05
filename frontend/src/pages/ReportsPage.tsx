import { RuntimeParametersBadge } from '../components/RuntimeParametersBadge'
import { useState } from 'react'
import { Modal } from '../components/Modal'
import {
  RuntimeParametersForm,
  metricParameterNames,
  runtimeParameterSource,
} from '../components/RuntimeParametersForm'
import type { Report } from '../types'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { Download, FolderOpen, Plus, Trash2 } from 'lucide-react'
import { api } from '../api'
import { EntityTabs } from '../components/EntityTabs'
import { useEntityContext } from './useEntityContext'

export function ReportsPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams()
  const context = useEntityContext(tenantId, entityId)
  const client = useQueryClient()
  const reports = useQuery({
    queryKey: ['reports', tenantId, entityId],
    queryFn: () => api.reports(tenantId, entityId),
  })
  const remove = useMutation({
    mutationFn: api.deleteReport.bind(null, tenantId, entityId),
    onSuccess: () => client.invalidateQueries({ queryKey: ['reports', tenantId, entityId] }),
  })
  const [exporting, setExporting] = useState<Report>()
  const exportMutation = useMutation({
    mutationFn: ({ reportId, parameters }: { reportId: string; parameters?: Record<string, unknown> }) =>
      exportCsv(reportId, parameters),
    onSuccess: () => setExporting(undefined),
  })
  async function exportCsv(reportId: string, parameters?: Record<string, unknown>) {
    const blob = await api.exportReport(tenantId, entityId, reportId, parameters)
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `report-${reportId}.csv`
    anchor.click()
    URL.revokeObjectURL(url)
  }
  if (context.isLoading) {
    return <p>Loading…</p>
  }
  if (context.error || reports.error) {
    return <p className="error">{(context.error ?? reports.error)?.message}</p>
  }
  return (
    <section className="page">
      <header>
        <div>
          <p className="eyebrow">Reports</p>
          <h2>{context.entity?.displayName}</h2>
        </div>
      </header>
      <EntityTabs entityId={entityId} />
      <div className="panel">
        <div className="panel-header">
          <h3>Saved reports</h3>
          <div className="actions">
            <span>
              {reports.data?.length ?? 0} {(reports.data?.length ?? 0) === 1 ? 'report' : 'reports'}
            </span>
            <Link className="button" to={`/entities/${entityId}/reports/new`}>
              <Plus />
              New report
            </Link>
          </div>
        </div>
        <div className="panel-body">
          {reports.data?.length ? (
            <ul className="definition-list">
              {reports.data.map((report) => (
                <li key={report.id}>
                  <div>
                    <strong>{report.name}</strong>
                    <RuntimeParametersBadge filter={runtimeParameterSource(report)} />
                    <small>{report.description || `${report.visualization} visualization`}</small>
                  </div>
                  <div className="actions">
                    <Link className="link" to={`/entities/${entityId}/reports/${report.id}`}>
                      <FolderOpen />
                      Open
                    </Link>
                    <button
                      className="link"
                      disabled={exportMutation.isPending}
                      onClick={() => {
                        exportMutation.reset()
                        if (metricParameterNames(runtimeParameterSource(report)).length) {
                          setExporting(report)
                        } else {
                          exportMutation.mutate({ reportId: report.id })
                        }
                      }}
                    >
                      <Download />
                      CSV
                    </button>
                    <button className="link danger" onClick={() => remove.mutate(report.id)}>
                      <Trash2 />
                      Delete
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <p className="empty">No reports yet.</p>
          )}
        </div>
      </div>
      {exportMutation.error && !exporting && <p className="error">{exportMutation.error.message}</p>}
      {exporting && (
        <Modal title={`Export ${exporting.name}`} onClose={() => setExporting(undefined)}>
          <RuntimeParametersForm
            names={metricParameterNames(runtimeParameterSource(exporting))}
            busy={exportMutation.isPending}
            submitLabel="Export CSV"
            onCancel={() => setExporting(undefined)}
            onEvaluate={(parameters) => exportMutation.mutate({ reportId: exporting.id, parameters })}
          />
          {exportMutation.error && (
            <p className="error" role="alert">
              {exportMutation.error.message}
            </p>
          )}
        </Modal>
      )}
    </section>
  )
}
