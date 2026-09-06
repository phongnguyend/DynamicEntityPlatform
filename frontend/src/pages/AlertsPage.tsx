import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import { EntityTabs } from '../components/EntityTabs'
import type { Alert, AlertAction, AlertComparisonOperator, AlertHistory, AlertInterval } from '../types'
import { useEntityContext } from './useEntityContext'
import { JsonEditorModal, requireJsonObject } from '../components/JsonEditorModal'

export function AlertsPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams()
  const context = useEntityContext(tenantId, entityId)
  const client = useQueryClient()
  const metrics = useQuery({ queryKey: ['metrics', tenantId, entityId], queryFn: () => api.metrics(tenantId, entityId) })
  const alerts = useQuery({ queryKey: ['alerts', tenantId, entityId], queryFn: () => api.alerts(tenantId, entityId) })
  const [jsonOpen, setJsonOpen] = useState(false)
  const [editing, setEditing] = useState<Alert>()
  const [name, setName] = useState('')
  const [metricId, setMetricId] = useState('')
  const [comparisonOperator, setComparison] = useState<AlertComparisonOperator>('GreaterThan')
  const [threshold, setThreshold] = useState(0)
  const [interval, setInterval] = useState<AlertInterval>('Hourly')
  const [cooldownSeconds, setCooldown] = useState(3600)
  const [notifyOnRecovery, setRecovery] = useState(true)
  const [isEnabled, setEnabled] = useState(true)
  const [emailEnabled, setEmailEnabled] = useState(false)
  const [emailRecipients, setEmailRecipients] = useState('')
  const [webhookEnabled, setWebhookEnabled] = useState(false)
  const [webhookUrls, setWebhookUrls] = useState([''])
  const [history, setHistory] = useState<AlertHistory>()

  const refresh = () => client.invalidateQueries({ queryKey: ['alerts', tenantId, entityId] })
  const configuredActions = (): AlertAction[] => [
    ...(emailEnabled ? [{ type: 'Email' as const, emailRecipients: emailRecipients.split(/[\n,;]/).map(value => value.trim()).filter(Boolean) }] : []),
    ...(webhookEnabled ? [{ type: 'Webhook' as const, webhookUrls: webhookUrls.map(value => value.trim()).filter(Boolean) }] : [])
  ]
  const save = useMutation({
    mutationFn: () => api.saveAlert(tenantId, entityId, { name, metricId, comparisonOperator, threshold, interval, timezone: 'UTC', cooldownSeconds, notifyOnRecovery, isEnabled, actions: configuredActions() }, editing?.id),
    onSuccess: () => { setEditing(undefined); setName(''); setEmailEnabled(false); setEmailRecipients(''); setWebhookEnabled(false); setWebhookUrls(['']); refresh() }
  })
  const remove = useMutation({ mutationFn: api.deleteAlert.bind(null, tenantId, entityId), onSuccess: refresh })

  function edit(alert: Alert) {
    const email = alert.actions.find(action => action.type === 'Email')
    const webhook = alert.actions.find(action => action.type === 'Webhook')
    setEditing(alert); setName(alert.name); setMetricId(alert.metricId); setComparison(alert.comparisonOperator)
    setThreshold(alert.threshold); setInterval(alert.interval); setCooldown(alert.cooldownSeconds)
    setRecovery(alert.notifyOnRecovery); setEnabled(alert.isEnabled); setEmailEnabled(Boolean(email))
    setEmailRecipients(email?.type === 'Email' ? email.emailRecipients.join(', ') : '')
    setWebhookEnabled(Boolean(webhook)); setWebhookUrls(webhook?.type === 'Webhook' ? webhook.webhookUrls : [''])
  }
  async function showHistory(alert: Alert) { setHistory(await api.alertHistory(tenantId, entityId, alert.id)) }
  async function test(alert: Alert) { await api.testAlert(tenantId, entityId, alert.id); await showHistory(alert); refresh() }
  async function saveJson(value: unknown) {
    const input = requireJsonObject(value, { metricId: 'string', name: 'string', comparisonOperator: 'string', threshold: 'number', interval: 'string', timezone: 'string', cooldownSeconds: 'number', notifyOnRecovery: 'boolean', isEnabled: 'boolean', actions: 'array' })
    const saved = await api.saveAlert(tenantId, entityId, input as unknown as Omit<Alert, 'id' | 'entityId' | 'lastState' | 'lastEvaluatedAt' | 'nextEvaluationAt' | 'createdAt' | 'updatedAt'>, editing?.id)
    edit(saved)
    await refresh()
  }
  if (context.isLoading) return <p>Loading…</p>

  return <section className="page">
    <header><div><p className="eyebrow">Scheduled alerts</p><h2>{context.entity?.displayName}</h2></div></header>
    <EntityTabs entityId={entityId} />
    <div className="workspace-grid">
      <div className="panel"><h3>{editing ? 'Edit alert' : 'New alert'}</h3><div className="designer">
        <label className="field">Name<input value={name} onChange={event => setName(event.target.value)} /></label>
        <label className="field">Metric<select value={metricId} onChange={event => setMetricId(event.target.value)}><option value="">Select a metric</option>{metrics.data?.map(metric => <option key={metric.id} value={metric.id}>{metric.name}</option>)}</select></label>
        <div className="threshold-row"><label className="field">Comparison<select value={comparisonOperator} onChange={event => setComparison(event.target.value as AlertComparisonOperator)}><option value="GreaterThan">&gt;</option><option value="GreaterThanOrEqual">≥</option><option value="LessThan">&lt;</option><option value="LessThanOrEqual">≤</option><option value="Equal">=</option><option value="NotEqual">≠</option></select></label><label className="field">Threshold<input type="number" value={threshold} onChange={event => setThreshold(Number(event.target.value))} /></label></div>
        <label className="field">Interval<select value={interval} onChange={event => setInterval(event.target.value as AlertInterval)}><option value="FiveMinutes">Every 5 minutes</option><option value="Hourly">Hourly</option><option value="Daily">Daily</option></select></label>
        <label className="field">Cooldown (seconds)<input type="number" min="0" value={cooldownSeconds} onChange={event => setCooldown(Number(event.target.value))} /></label>
        <fieldset className="alert-actions"><legend>Actions</legend><p className="field-note">Configuration is saved only. Email and webhook delivery are not active yet.</p>
          <label className="check"><input type="checkbox" checked={emailEnabled} onChange={event => setEmailEnabled(event.target.checked)} />Email</label>
          {emailEnabled && <label className="field">Recipients<input type="text" placeholder="ops@example.com, owner@example.com" value={emailRecipients} onChange={event => setEmailRecipients(event.target.value)} /></label>}
          <label className="check"><input type="checkbox" checked={webhookEnabled} onChange={event => setWebhookEnabled(event.target.checked)} />Webhook</label>
          {webhookEnabled && <div className="webhook-url-list">{webhookUrls.map((url, index) => <div className="webhook-url-row" key={index}><label className="field">Webhook URL {index + 1}<input type="url" placeholder="https://example.com/alerts" value={url} onChange={event => setWebhookUrls(values => values.map((value, current) => current === index ? event.target.value : value))} /></label>{webhookUrls.length > 1 && <button type="button" className="link danger icon-only" aria-label={`Remove webhook URL ${index + 1}`} title="Remove webhook URL" onClick={() => setWebhookUrls(values => values.filter((_, current) => current !== index))}><Trash2 /></button>}</div>)}<button type="button" className="secondary icon-only justify-self-start" aria-label="Add webhook URL" title="Add webhook URL" disabled={webhookUrls.length >= 20} onClick={() => setWebhookUrls(values => [...values, ''])}><Plus /></button></div>}
        </fieldset>
        <label className="check"><input type="checkbox" checked={notifyOnRecovery} onChange={event => setRecovery(event.target.checked)} />Notify on recovery</label>
        <label className="check"><input type="checkbox" checked={isEnabled} onChange={event => setEnabled(event.target.checked)} />Enabled</label>
        <div className="actions"><button disabled={!name || !metricId || emailEnabled && !emailRecipients.trim() || webhookEnabled && !webhookUrls.some(value => value.trim())} onClick={() => save.mutate()}>Save alert</button><button className="secondary" onClick={() => setJsonOpen(true)}>Edit JSON</button>{editing && <button className="secondary" onClick={() => setEditing(undefined)}>Cancel</button>}</div>
        {save.error && <p className="error">{save.error.message}</p>}
      </div></div>
      <div className="panel wide"><h3>Alerts</h3><ul className="definition-list">{alerts.data?.map(alert => <li key={alert.id}><div><strong>{alert.name}</strong><small><span className={`alert-state ${alert.lastState?.toLowerCase() ?? 'normal'}`}>{alert.lastState ?? 'Not evaluated'}</span> · {alert.interval} · {alert.isEnabled ? 'Enabled' : 'Disabled'} · {alert.actions.length ? alert.actions.map(action => action.type).join(', ') : 'In-app only'}</small></div><div className="actions"><button className="secondary" onClick={() => test(alert)}>Test now</button><button className="link" onClick={() => showHistory(alert)}>History</button><button className="link" onClick={() => edit(alert)}>Edit</button><button className="link danger" onClick={() => remove.mutate(alert.id)}>Delete</button></div></li>)}</ul>
        {history && <div className="history"><h3>Evaluation history</h3><table><thead><tr><th>Time</th><th>Value</th><th>Threshold</th><th>State</th><th>Error</th></tr></thead><tbody>{history.evaluations.map(item => <tr key={item.id}><td>{new Date(item.evaluatedAt).toLocaleString()}</td><td>{String(item.value ?? '—')}</td><td>{item.threshold}</td><td>{item.state}</td><td>{item.error ?? ''}</td></tr>)}</tbody></table><p className="record-count">{history.notifications.length} in-app notification(s)</p></div>}
      </div>
    </div>
    {jsonOpen && <JsonEditorModal title={`${editing ? 'Edit' : 'Create'} alert as JSON`} value={{ metricId, name, comparisonOperator, threshold, interval, timezone: 'UTC', cooldownSeconds, notifyOnRecovery, isEnabled, actions: configuredActions() }} onClose={() => setJsonOpen(false)} onSave={saveJson} />}
  </section>
}
