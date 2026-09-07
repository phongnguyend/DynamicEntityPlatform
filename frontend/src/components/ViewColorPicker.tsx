import { useEffect, useId, useState } from 'react'
import { Ban, Check, Pipette } from 'lucide-react'
import { ColorPicker, isLightColor } from './ColorPicker'

export const VIEW_COLORS = [
  { value: '#176b4d', label: 'Green' },
  { value: '#0f7a80', label: 'Teal' },
  { value: '#1d5fa8', label: 'Blue' },
  { value: '#6b3fa0', label: 'Purple' },
  { value: '#a42e2e', label: 'Red' },
  { value: '#b4761b', label: 'Amber' },
  { value: '#4a5a52', label: 'Slate' },
]

// Any hex color is allowed — the presets are only quick picks, and a definition edited by hand
// through the JSON view can carry its own. Anything that is not a hex color reads as "no color"
// rather than being pushed into a style attribute.
const HEX_COLOR = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i

function normalizeColor(color: string) {
  const trimmed = color.trim()
  return HEX_COLOR.test(trimmed) ? trimmed.toLowerCase() : undefined
}

export function getViewColor(definition: Record<string, unknown> | null | undefined) {
  const color = definition?.color
  return typeof color === 'string' ? normalizeColor(color) : undefined
}

export function withViewColor(definition: Record<string, unknown>, color: string | undefined) {
  return { ...definition, color: color ?? undefined }
}

const DEFAULT_CUSTOM_COLOR = '#176b4d'

export function ViewColorPicker({ color, onChange }: { color?: string; onChange: (color?: string) => void }) {
  const [hex, setHex] = useState(color ?? '')
  // Follow the swatches and the picker pad, but leave a half-typed hex alone.
  useEffect(() => setHex(current => normalizeColor(current) === color ? current : color ?? ''), [color])
  const isCustom = Boolean(color) && !VIEW_COLORS.some(option => option.value === color)
  const [pickerOpen, setPickerOpen] = useState(isCustom)
  const invalid = hex.trim().length > 0 && !normalizeColor(hex)
  const panelId = useId()

  return <fieldset className="view-color-picker">
    <legend>Color</legend>
    <div className="view-color-options">
      <button type="button" className={`view-color-option none${color ? '' : ' selected'}`}
        title="No color" aria-label="No color" aria-pressed={!color} onClick={() => onChange(undefined)}><Ban /></button>
      {VIEW_COLORS.map(option => <button key={option.value} type="button"
        className={`view-color-option${color === option.value ? ' selected' : ''}`} style={{ background: option.value }}
        title={option.label} aria-label={option.label} aria-pressed={color === option.value}
        onClick={() => onChange(option.value)}>{color === option.value && <Check />}</button>)}
      <button type="button" className={`view-color-option custom${isCustom ? ' selected' : ''}`}
        style={isCustom ? { background: color, color: isLightColor(color!) ? undefined : '#fff' } : undefined}
        title="Custom color" aria-label="Custom color" aria-expanded={pickerOpen} aria-controls={panelId}
        onClick={() => setPickerOpen(current => !current)}>{isCustom ? <Check /> : <Pipette />}</button>
    </div>
    {pickerOpen && <div id={panelId} className="view-color-panel">
      <ColorPicker color={color ?? DEFAULT_CUSTOM_COLOR} onChange={onChange} />
      <label className="field view-color-hex">Hex code
        <input value={hex} placeholder={DEFAULT_CUSTOM_COLOR} maxLength={7} spellCheck={false} aria-invalid={invalid}
          onChange={event => {
            setHex(event.target.value)
            if (!event.target.value.trim()) onChange(undefined)
            else {
              const normalized = normalizeColor(event.target.value)
              if (normalized) onChange(normalized)
            }
          }} />
      </label>
    </div>}
  </fieldset>
}

export function ViewColorDot({ color }: { color?: string }) {
  if (!color) return null
  return <span className="view-color-dot" style={{ background: color }} aria-hidden="true" />
}
