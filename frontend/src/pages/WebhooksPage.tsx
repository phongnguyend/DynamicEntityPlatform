import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useParams } from 'react-router-dom'
import { Pencil, Power, PowerOff, Save, Trash2, X } from 'lucide-react'
import { EntityTabs } from '../components/EntityTabs'
import { api } from '../api'
import type { WebhookEvent, WebhookSubscription } from '../types'
import { useEntityContext } from './useEntityContext'

const availableEvents: Array<{ value: WebhookEvent; label: string }> = [
  { value: 'RecordCreated', label: 'Record created' },
  { value: 'RecordUpdated', label: 'Record updated' },
  { value: 'RecordDeleted', label: 'Record deleted' },
]

export function WebhooksPage({ tenantId }: { tenantId: string }) {
  const { entityId = '' } = useParams()
  const context = useEntityContext(tenantId, entityId)
  const client = useQueryClient()
  const subscriptions = useQuery({ queryKey: ['webhooks', tenantId, entityId], queryFn: () => api.webhooks(tenantId, entityId), enabled: Boolean(entityId) })
  const [editing, setEditing] = useState<WebhookSubscription>()
  const [name, setName] = useState('')
  const [endpoint, setEndpoint] = useState('')
  const [events, setEvents] = useState<WebhookEvent[]>(['RecordCreated'])
  const [isEnabled, setEnabled] = useState(true)

  const refresh = () => client.invalidateQueries({ queryKey: ['webhooks', tenantId, entityId] })
  const save = useMutation({ mutationFn: () => api.saveWebhook(tenantId, entityId, { name, endpoint, events, isEnabled }, editing?.id), onSuccess: async () => { reset(); await refresh() } })
  const remove = useMutation({ mutationFn: api.deleteWebhook.bind(null, tenantId, entityId), onSuccess: refresh })
  const toggle = useMutation({ mutationFn: (subscription: WebhookSubscription) => api.saveWebhook(tenantId, entityId, { name: subscription.name, endpoint: subscription.endpoint, events: subscription.events, isEnabled: !subscription.isEnabled }, subscription.id), onSuccess: refresh })

  function reset() { setEditing(undefined); setName(''); setEndpoint(''); setEvents(['RecordCreated']); setEnabled(true) }
  function edit(subscription: WebhookSubscription) { setEditing(subscription); setName(subscription.name); setEndpoint(subscription.endpoint); setEvents(subscription.events); setEnabled(subscription.isEnabled) }
  function selectEvent(value: WebhookEvent, selected: boolean) { setEvents(current => selected ? [...new Set([...current, value])] : current.filter(item => item !== value)) }

  if (context.isLoading) return <p>Loading…</p>
  return <section className="page"><header><div><p className="eyebrow">Event subscriptions</p><h2>{context.entity?.displayName}</h2></div></header><EntityTabs entityId={entityId} />
    <div className="workspace-grid"><div className="panel"><div className="panel-header"><h3>{editing ? 'Edit webhook' : 'New webhook'}</h3></div><div className="panel-body designer">
      <label className="field">Name<input value={name} maxLength={200} onChange={event => setName(event.target.value)} placeholder="CRM record changes" /></label>
      <label className="field">Endpoint URL<input type="url" value={endpoint} onChange={event => setEndpoint(event.target.value)} placeholder="https://example.com/webhooks/records" /></label>
      <fieldset className="event-picker"><legend>Events</legend>{availableEvents.map(item => <label className="check" key={item.value}><input type="checkbox" checked={events.includes(item.value)} onChange={event => selectEvent(item.value, event.target.checked)} />{item.label}</label>)}</fieldset>
      <label className="check"><input type="checkbox" checked={isEnabled} onChange={event => setEnabled(event.target.checked)} />Enabled</label>
      <p className="webhook-note">Delivery is not active yet. These settings will be used when webhook firing is implemented.</p>
      {save.error && <p className="error">{save.error.message}</p>}
    </div>
      <div className="panel-footer"><button disabled={!name.trim() || !endpoint.trim() || events.length === 0 || save.isPending} onClick={() => save.mutate()}><Save />{save.isPending ? 'Saving…' : 'Save webhook'}</button>{editing && <button className="secondary" onClick={reset}><X />Cancel</button>}</div>
    </div>
    <div className="panel wide"><div className="panel-header"><h3>Webhooks</h3><span>{subscriptions.data?.length ?? 0} {(subscriptions.data?.length ?? 0) === 1 ? 'subscription' : 'subscriptions'}</span></div>
      <div className="panel-body">{subscriptions.error ? <p className="error">{subscriptions.error.message}</p> : subscriptions.data?.length ? <ul className="definition-list">{subscriptions.data.map(subscription => <li key={subscription.id}><div><strong>{subscription.name}</strong><small className="webhook-endpoint">{subscription.endpoint}</small><small>{subscription.events.map(value => availableEvents.find(item => item.value === value)?.label ?? value).join(' · ')} · {subscription.isEnabled ? 'Enabled' : 'Disabled'}</small></div><div className="actions"><button className="secondary" onClick={() => toggle.mutate(subscription)}>{subscription.isEnabled ? <PowerOff /> : <Power />}{subscription.isEnabled ? 'Disable' : 'Enable'}</button><button className="link" onClick={() => edit(subscription)}><Pencil />Edit</button><button className="link danger" onClick={() => remove.mutate(subscription.id)}><Trash2 />Delete</button></div></li>)}</ul> : <p className="empty">No webhook subscriptions yet.</p>}
      {(remove.error || toggle.error) && <p className="error">{remove.error?.message ?? toggle.error?.message}</p>}
      </div>
    </div></div>
  </section>
}
