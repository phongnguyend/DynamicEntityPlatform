import { useState, type DragEvent } from 'react'
import { Columns3, GripVertical, X } from 'lucide-react'
import { Modal } from './Modal'

export interface ViewColumnSettings {
  order: string[]
  hidden: string[]
}

export function ColumnSettingsModal({
  columns,
  settings,
  onClose,
  onApply,
  description = 'Drag columns into display order and choose which columns are visible. Use Save to persist the changes.',
}: {
  columns: { id: string; displayName: string; name: string; dataType: string }[]
  settings: ViewColumnSettings
  onClose: () => void
  onApply: (settings: ViewColumnSettings) => void | Promise<void>
  description?: string
}) {
  const fieldsById = new Map(columns.map((field) => [field.id, field]))
  const [order, setOrder] = useState(settings.order)
  const [hidden, setHidden] = useState(() => new Set(settings.hidden))
  const [draggedId, setDraggedId] = useState<string>()
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function apply() {
    setSaving(true)
    setError('')
    try {
      await onApply({ order, hidden: [...hidden] })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not apply column settings.')
    } finally {
      setSaving(false)
    }
  }

  function dragOver(event: DragEvent<HTMLLIElement>, targetId: string) {
    event.preventDefault()
    if (!draggedId || draggedId === targetId) {
      return
    }
    setOrder((current) => {
      const from = current.indexOf(draggedId)
      const to = current.indexOf(targetId)
      if (from < 0 || to < 0) {
        return current
      }
      const next = current.slice()
      const [fieldId] = next.splice(from, 1)
      next.splice(to, 0, fieldId)
      return next
    })
  }

  function toggle(fieldId: string) {
    setHidden((current) => {
      const next = new Set(current)
      if (next.has(fieldId)) {
        next.delete(fieldId)
      } else {
        next.add(fieldId)
      }
      return next
    })
  }

  return (
    <Modal title="Column settings" onClose={onClose}>
      <div className="column-settings">
        <p className="field-note">{description}</p>
        <ol className="field-order-list">
          {order.map((fieldId) => {
            const field = fieldsById.get(fieldId)
            if (!field) {
              return null
            }
            return (
              <li
                key={fieldId}
                draggable={!saving}
                className={draggedId === fieldId ? 'dragging' : ''}
                onDragStart={(event) => {
                  setDraggedId(fieldId)
                  event.dataTransfer.effectAllowed = 'move'
                  event.dataTransfer.setData('text/plain', fieldId)
                }}
                onDragOver={(event) => dragOver(event, fieldId)}
                onDragEnd={() => setDraggedId(undefined)}
              >
                <span className="drag-handle" aria-hidden="true">
                  <GripVertical />
                </span>
                <label className="check">
                  <input
                    type="checkbox"
                    aria-label={field.displayName}
                    disabled={saving}
                    checked={!hidden.has(fieldId)}
                    onChange={() => toggle(fieldId)}
                  />
                  <span>
                    <strong>{field.displayName}</strong>
                    <small>
                      {field.name} · {field.dataType}
                    </small>
                  </span>
                </label>
                <span className={hidden.has(fieldId) ? 'column-hidden' : 'index-status'}>
                  {hidden.has(fieldId) ? 'Hidden' : 'Visible'}
                </span>
              </li>
            )
          })}
        </ol>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <div className="modal-footer">
          <button type="button" disabled={saving} onClick={apply}>
            <Columns3 aria-hidden="true" />
            {saving ? 'Applying...' : 'Apply settings'}
          </button>
          <button type="button" className="secondary" disabled={saving} onClick={onClose}>
            <X aria-hidden="true" />
            Cancel
          </button>
        </div>
      </div>
    </Modal>
  )
}
