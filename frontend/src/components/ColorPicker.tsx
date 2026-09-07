import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'

interface Hsv { h: number; s: number; v: number }

const clamp = (value: number, max = 1) => Math.min(max, Math.max(0, value))

export function hexToRgb(hex: string) {
  const raw = hex.trim().replace('#', '')
  const full = raw.length === 3 ? raw.split('').map(part => part + part).join('') : raw
  const int = Number.parseInt(full, 16)
  return { r: (int >> 16) & 255, g: (int >> 8) & 255, b: int & 255 }
}

function rgbToHex(r: number, g: number, b: number) {
  return `#${[r, g, b].map(part => Math.round(part).toString(16).padStart(2, '0')).join('')}`
}

function hexToHsv(hex: string): Hsv {
  const { r, g, b } = hexToRgb(hex)
  const [red, green, blue] = [r / 255, g / 255, b / 255]
  const max = Math.max(red, green, blue)
  const span = max - Math.min(red, green, blue)
  const hue = span === 0 ? 0
    : max === red ? 60 * (((green - blue) / span) % 6)
    : max === green ? 60 * ((blue - red) / span + 2)
    : 60 * ((red - green) / span + 4)
  return { h: (hue + 360) % 360, s: max === 0 ? 0 : span / max, v: max }
}

function hsvToHex({ h, s, v }: Hsv) {
  const channel = (offset: number) => {
    const k = (offset + h / 60) % 6
    return 255 * v * (1 - s * Math.max(0, Math.min(k, 4 - k, 1)))
  }
  return rgbToHex(channel(5), channel(3), channel(1))
}

/** True for colors light enough that a marker or label on top needs to be dark. */
export function isLightColor(hex: string) {
  const { r, g, b } = hexToRgb(hex)
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.6
}

/**
 * Saturation/value pad plus a hue slider, driven in HSV. Hue is held in state rather than derived
 * from the hex on every render, so dragging into pure black or white does not lose the hue the
 * user picked. The pad reports on pointer drag; the hue is a range input so it stays keyboard
 * accessible for free.
 */
export function ColorPicker({ color, onChange }: { color: string; onChange: (color: string) => void }) {
  const [hsv, setHsv] = useState(() => hexToHsv(color))
  // Re-seed only when the incoming color is not the one this state already describes.
  useEffect(() => setHsv(current => hsvToHex(current) === color ? current : hexToHsv(color)), [color])
  const padRef = useRef<HTMLDivElement>(null)

  function commit(next: Hsv) {
    setHsv(next)
    onChange(hsvToHex(next))
  }

  function track(event: PointerEvent<HTMLDivElement>) {
    const rect = padRef.current?.getBoundingClientRect()
    if (!rect) return
    commit({
      ...hsv,
      s: clamp((event.clientX - rect.left) / rect.width),
      v: 1 - clamp((event.clientY - rect.top) / rect.height),
    })
  }

  function nudge(event: KeyboardEvent<HTMLDivElement>) {
    const step = event.shiftKey ? 0.1 : 0.02
    const delta = { ArrowLeft: { s: -step }, ArrowRight: { s: step }, ArrowUp: { v: step }, ArrowDown: { v: -step } }[event.key]
    if (!delta) return
    event.preventDefault()
    commit({ ...hsv, s: clamp(hsv.s + (delta.s ?? 0)), v: clamp(hsv.v + (delta.v ?? 0)) })
  }

  const hueColor = hsvToHex({ h: hsv.h, s: 1, v: 1 })
  return <div className="color-picker">
    <div ref={padRef} className="color-pad" tabIndex={0} role="group"
      aria-label="Saturation and brightness. Use the arrow keys to adjust."
      style={{ background: `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, ${hueColor})` }}
      onKeyDown={nudge}
      onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); track(event) }}
      onPointerMove={event => { if (event.buttons & 1) track(event) }}>
      <span className="color-pad-thumb" style={{ left: `${hsv.s * 100}%`, top: `${(1 - hsv.v) * 100}%`, background: color }} />
    </div>
    <input className="color-hue" type="range" min={0} max={359} step={1} value={Math.round(hsv.h)}
      aria-label="Hue" onChange={event => commit({ ...hsv, h: Number(event.target.value) })} />
  </div>
}
