import { useState } from 'react'

// A newly added reference has an empty name, but still requires manual evaluation.
export function hasRuntimeParameterReferences(value: unknown): boolean {
  if (!value || typeof value !== 'object') {
    return false
  }
  if (Array.isArray(value)) {
    return value.some(hasRuntimeParameterReferences)
  }
  return (
    Object.prototype.hasOwnProperty.call(value, 'parameter') || Object.values(value).some(hasRuntimeParameterReferences)
  )
}

export function metricParameterNames(filter: unknown, requiredOnly = false): string[] {
  const names = new Set<string>()
  function visit(value: unknown) {
    if (!value || typeof value !== 'object') {
      return
    }
    if (Array.isArray(value)) {
      value.forEach(visit)
      return
    }
    const node = value as Record<string, unknown>
    if (
      typeof node.parameter === 'string' &&
      node.parameter.trim() &&
      (!requiredOnly || !Object.prototype.hasOwnProperty.call(node, 'defaultValue'))
    ) {
      names.add(node.parameter)
    }
    Object.values(node).forEach(visit)
  }
  visit(filter)
  return [...names]
}

export function RuntimeParametersForm({
  names,
  busy,
  onEvaluate,
  onCancel,
  submitLabel = 'Evaluate metric',
  optional = false,
  initialValues = {},
}: {
  names: string[]
  busy: boolean
  onEvaluate: (parameters: Record<string, unknown>) => void
  onCancel?: () => void
  submitLabel?: string
  optional?: boolean
  initialValues?: Record<string, unknown>
}) {
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(Object.entries(initialValues).map(([name, value]) => [name, String(value ?? '')])),
  )
  return (
    <form
      className="dynamic-form"
      onSubmit={(event) => {
        event.preventDefault()
        const parameters: Record<string, unknown> = {}
        for (const name of names) {
          if (Object.prototype.hasOwnProperty.call(values, name) && (!optional || values[name] !== '')) {
            parameters[name] = values[name]
          }
        }
        onEvaluate(parameters)
      }}
    >
      <p className="field-note">
        {optional
          ? 'Values are optional. Leave blank to use a default or show an unbound parameter.'
          : 'Enter a value for each parameter.'}{' '}
        Text is used exactly as entered. For date fields use YYYY-MM-DD; for boolean fields use true or false.
      </p>
      {names.map((name, index) => (
        <label className="field" key={name}>
          {name}
          <input
            autoFocus={index === 0}
            disabled={busy}
            placeholder={`Enter ${name}`}
            value={values[name] ?? ''}
            onChange={(event) => setValues((current) => ({ ...current, [name]: event.target.value }))}
          />
        </label>
      ))}
      <div className="actions">
        {onCancel && (
          <button type="button" className="secondary" disabled={busy} onClick={onCancel}>
            Cancel
          </button>
        )}
        <button type="submit" disabled={busy}>
          {busy ? 'Processing…' : submitLabel}
        </button>
      </div>
    </form>
  )
}
