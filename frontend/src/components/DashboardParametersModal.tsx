import { useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Pencil, Plus, SlidersHorizontal, Trash2, X, Save, Check } from 'lucide-react'
import type { DashboardParameter } from '../types'
import { Modal } from './Modal'
import './DashboardParametersModal.css'

const parameterTypes: Record<DashboardParameter['type'], string> = {
  text: 'Text',
  number: 'Number',
  boolean: 'Boolean',
  date: 'Date',
  'datetime-local': 'Date and time',
}

export function DashboardParametersModal({
  parameters,
  onClose,
  onSave,
}: {
  parameters: DashboardParameter[]
  onClose: () => void
  onSave: (parameters: DashboardParameter[]) => Promise<void>
}) {
  const [items, setItems] = useState(parameters)
  const [editing, setEditing] = useState<DashboardParameter>()
  const [mode, setMode] = useState<'edit' | 'configure'>('edit')
  const [deleting, setDeleting] = useState<string>()
  const save = useMutation({ mutationFn: () => onSave(items) })

  function edit(parameter: DashboardParameter, nextMode: 'edit' | 'configure') {
    setEditing({ ...parameter })
    setMode(nextMode)
    setDeleting(undefined)
    save.reset()
  }

  return (
    <Modal
      title="Dashboard parameters"
      onClose={() => {
        if (!save.isPending) {
          onClose()
        }
      }}
    >
      <div className="dashboard-parameters">
        <p className="field-note">
          Manage values shared by this dashboard. Bind them using each card's Configure runtime parameters action.
          Changing a value updates bound cards; deleting or clearing a parameter prevents those cards from running until
          configured again.
        </p>
        {editing ? (
          <DashboardParameterForm
            key={`${editing.id}:${mode}`}
            parameter={editing}
            parameters={items}
            configureOnly={mode === 'configure'}
            onCancel={() => setEditing(undefined)}
            onApply={(parameter) => {
              setItems((current) =>
                current.some((item) => item.id === parameter.id)
                  ? current.map((item) => (item.id === parameter.id ? parameter : item))
                  : [...current, parameter],
              )
              setEditing(undefined)
            }}
          />
        ) : (
          <>
            <div className="dashboard-parameters-toolbar">
              <span>{items.length} parameters</span>
              <button
                type="button"
                disabled={save.isPending}
                onClick={() =>
                  edit(
                    {
                      id: crypto.randomUUID(),
                      name: '',
                      label: '',
                      description: '',
                      type: 'text',
                    },
                    'edit',
                  )
                }
              >
                <Plus />
                Add parameter
              </button>
            </div>
            {!items.length && (
              <p className="empty">No dashboard parameters yet. Add one to define a name, type, and value.</p>
            )}
            <ul className="dashboard-parameters-list">
              {items.map((parameter) => (
                <li key={parameter.id}>
                  <div className="dashboard-parameter-summary">
                    <strong>{parameter.label || parameter.name}</strong>
                    <small>
                      {parameter.name} · {parameterTypes[parameter.type]}
                    </small>
                    {parameter.description && <p>{parameter.description}</p>}
                    <span>
                      Value:{' '}
                      {parameter.value === undefined
                        ? 'Not configured'
                        : parameter.value === ''
                          ? '(empty text)'
                          : String(parameter.value)}
                    </span>
                  </div>
                  <div className="dashboard-parameter-actions">
                    <button
                      type="button"
                      className="link icon-only"
                      title="Configure value"
                      aria-label={`Configure ${parameter.name}`}
                      disabled={save.isPending}
                      onClick={() => edit(parameter, 'configure')}
                    >
                      <SlidersHorizontal />
                    </button>
                    <button
                      type="button"
                      className="link icon-only"
                      title="Edit parameter"
                      aria-label={`Edit ${parameter.name}`}
                      disabled={save.isPending}
                      onClick={() => edit(parameter, 'edit')}
                    >
                      <Pencil />
                    </button>
                    <button
                      type="button"
                      className="link danger icon-only"
                      title="Delete parameter"
                      aria-label={`Delete ${parameter.name}`}
                      disabled={save.isPending}
                      onClick={() => setDeleting(parameter.id)}
                    >
                      <Trash2 />
                    </button>
                  </div>
                  {deleting === parameter.id && (
                    <div className="dashboard-parameter-delete">
                      <span>Delete {parameter.name}?</span>
                      <button type="button" className="secondary" onClick={() => setDeleting(undefined)}>
                        <X aria-hidden="true" />
                        Cancel
                      </button>
                      <button
                        type="button"
                        className="danger"
                        onClick={() => {
                          setItems((current) => current.filter((item) => item.id !== parameter.id))
                          setDeleting(undefined)
                        }}
                      >
                        <Trash2 aria-hidden="true" />
                        Delete parameter
                      </button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
            {save.error && (
              <p className="error" role="alert">
                {save.error.message}
              </p>
            )}
            <p className="field-note">Changes are saved only when you select Save changes.</p>
            <div className="modal-footer">
              <button type="button" className="secondary" disabled={save.isPending} onClick={onClose}>
                <X aria-hidden="true" />
                Cancel
              </button>
              <button type="button" disabled={save.isPending || !!deleting} onClick={() => save.mutate()}>
                <Save aria-hidden="true" />
                {save.isPending ? 'Saving…' : 'Save changes'}
              </button>
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}

function DashboardParameterForm({
  parameter,
  parameters,
  configureOnly,
  onCancel,
  onApply,
}: {
  parameter: DashboardParameter
  parameters: DashboardParameter[]
  configureOnly: boolean
  onCancel: () => void
  onApply: (parameter: DashboardParameter) => void
}) {
  const [draft, setDraft] = useState(parameter)
  const [configured, setConfigured] = useState(parameter.value !== undefined)
  const [value, setValue] = useState(String(parameter.value ?? ''))
  const name = draft.name.trim()
  const duplicate = parameters.some((item) => item.id !== draft.id && item.name.toLowerCase() === name.toLowerCase())
  const validName = /^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(name)

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!validName || duplicate) {
      return
    }
    let parsed: DashboardParameter['value'] = undefined
    if (configured) {
      parsed = draft.type === 'number' ? Number(value) : draft.type === 'boolean' ? value === 'true' : value
      if (typeof parsed === 'number' && !Number.isFinite(parsed)) {
        return
      }
    }
    onApply({ ...draft, name, label: draft.label.trim(), description: draft.description.trim(), value: parsed })
  }

  return (
    <form className="dynamic-form" onSubmit={submit}>
      <h4>
        {configureOnly
          ? `Configure ${parameter.label || parameter.name}`
          : parameter.name
            ? 'Edit parameter'
            : 'Add parameter'}
      </h4>
      {!configureOnly && (
        <>
          <label className="field">
            Name
            <input
              autoFocus
              required
              maxLength={64}
              pattern="[A-Za-z][A-Za-z0-9_]{0,63}"
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              placeholder="e.g. customerId"
            />
          </label>
          <p className="field-note">
            Start with a letter; use letters, digits, or underscores. Names must be unique within this dashboard.
          </p>
          {duplicate && (
            <p className="error" role="alert">
              A parameter with this name already exists.
            </p>
          )}
          <label className="field">
            Display label
            <input
              value={draft.label}
              onChange={(event) => setDraft({ ...draft, label: event.target.value })}
              placeholder="e.g. Customer"
            />
          </label>
          <label className="field">
            Description
            <textarea
              value={draft.description}
              onChange={(event) => setDraft({ ...draft, description: event.target.value })}
            />
          </label>
          <label className="field">
            Type
            <select
              value={draft.type}
              onChange={(event) => {
                setDraft({ ...draft, type: event.target.value as DashboardParameter['type'] })
                setConfigured(false)
                setValue('')
              }}
            >
              {Object.entries(parameterTypes).map(([type, label]) => (
                <option key={type} value={type}>
                  {label}
                </option>
              ))}
            </select>
          </label>
        </>
      )}
      <label className="dashboard-parameter-value-checkbox">
        <input type="checkbox" checked={configured} onChange={(event) => setConfigured(event.target.checked)} />
        Set a value
      </label>
      {configured && (
        <label className="field">
          Value
          {draft.type === 'boolean' ? (
            <select required value={value} onChange={(event) => setValue(event.target.value)}>
              <option value="" disabled>
                Select a value
              </option>
              <option value="true">True</option>
              <option value="false">False</option>
            </select>
          ) : (
            <input
              type={draft.type}
              step={draft.type === 'number' ? 'any' : undefined}
              required={draft.type !== 'text'}
              value={value}
              onChange={(event) => setValue(event.target.value)}
            />
          )}
        </label>
      )}
      <div className="actions">
        <button type="button" className="secondary" onClick={onCancel}>
          <X aria-hidden="true" />
          Cancel
        </button>
        <button type="submit" disabled={!validName || duplicate}>
          <Check aria-hidden="true" />
          Apply parameter
        </button>
      </div>
    </form>
  )
}
