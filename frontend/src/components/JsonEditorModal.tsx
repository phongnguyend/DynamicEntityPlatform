import { useState } from 'react'
import { Modal } from './Modal'
import { Save, X } from 'lucide-react'

export function JsonEditorModal({ title, value, onClose, onSave }: {
  title: string
  value: unknown
  onClose: () => void
  onSave: (value: unknown) => Promise<void>
}) {
  const [json, setJson] = useState(() => JSON.stringify(value, null, 2))
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)

  async function save() {
    let parsed: unknown
    try {
      parsed = JSON.parse(json)
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        throw new Error('The definition must be a JSON object.')
      }
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The definition is not valid JSON.')
      return
    }

    setSaving(true)
    setError('')
    try {
      await onSave(parsed)
      onClose()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The definition could not be saved.')
    } finally {
      setSaving(false)
    }
  }

  return <Modal title={title} onClose={onClose}>
    <div className="json-editor">
      <p className="field-note">Edit the persisted definition payload. Server-side validation still applies.</p>
      <label className="field">JSON
        <textarea value={json} onChange={event => setJson(event.target.value)} spellCheck={false} />
      </label>
      {error && <p className="error">{error}</p>}
      <div className="actions">
        <button type="button" disabled={saving} onClick={save}><Save />{saving ? 'Saving…' : 'Save JSON'}</button>
        <button type="button" className="secondary" disabled={saving} onClick={onClose}><X />Cancel</button>
      </div>
    </div>
  </Modal>
}

export function requireJsonObject(value: unknown, fields: Record<string, 'string' | 'number' | 'boolean' | 'object' | 'array'>) {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error('The definition must be a JSON object.')
  const object = value as Record<string, unknown>
  for (const [field, type] of Object.entries(fields)) {
    if (type === 'array' ? !Array.isArray(object[field]) : typeof object[field] !== type || type === 'object' && (object[field] === null || Array.isArray(object[field]))) {
      throw new Error(`Property "${field}" must be a ${type}.`)
    }
  }
  return object
}
