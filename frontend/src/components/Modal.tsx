import type { ReactNode } from 'react'

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  return <div className="modal-backdrop" onClick={onClose}>
    <div className="modal" onClick={event => event.stopPropagation()}>
      <div className="modal-header"><h3>{title}</h3><button type="button" className="link" onClick={onClose}>Close</button></div>
      <div className="modal-body">{children}</div>
    </div>
  </div>
}
