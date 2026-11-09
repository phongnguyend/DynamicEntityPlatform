import type { ReactNode } from 'react'
import './Modal.css'
import { X } from 'lucide-react'

export function Modal({
  title,
  onClose,
  wide,
  role = 'dialog',
  descriptionId,
  children,
}: {
  title: string
  onClose: () => void
  wide?: boolean
  role?: 'dialog' | 'alertdialog'
  descriptionId?: string
  children: ReactNode
}) {
  return (
    <div className="modal-backdrop">
      <div
        className={wide ? 'modal wide' : 'modal'}
        role={role}
        aria-modal="true"
        aria-label={title}
        aria-describedby={descriptionId}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="modal-header">
          <h3>{title}</h3>
          <button type="button" className="link icon-only" aria-label="Close dialog" title="Close" onClick={onClose}>
            <X aria-hidden="true" />
          </button>
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  )
}
