import { CommonModule } from '@angular/common';
import { Component, HostListener, inject, signal } from '@angular/core';
import { ApiErrorReportService } from './api-error-report.service';

@Component({
  selector: 'app-api-error-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (svc.dialog(); as m) {
      <div class="backdrop" role="presentation" (click)="close()"></div>
      <div class="dialog" role="alertdialog" aria-modal="true" aria-labelledby="api-err-title">
        <header class="hdr">
          <h2 id="api-err-title">Request failed</h2>
          <button type="button" class="icon-close" (click)="close()" aria-label="Close">×</button>
        </header>
        <p class="headline">{{ m.headline }}</p>
        <div class="actions">
          <button type="button" (click)="copy(m.fullReport)">
            {{ copyHint() }}
          </button>
          <button type="button" class="secondary" (click)="close()">Close</button>
        </div>
        <label class="lbl" for="api-err-pre">Full report (copy-friendly)</label>
        <pre id="api-err-pre" class="body" tabindex="0">{{ m.fullReport }}</pre>
      </div>
    }
  `,
  styles: [
    `
      .backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.55);
        z-index: 9000;
      }
      .dialog {
        position: fixed;
        left: 50%;
        top: 50%;
        transform: translate(-50%, -50%);
        width: min(720px, calc(100vw - 2rem));
        max-height: min(85vh, 640px);
        display: flex;
        flex-direction: column;
        background: var(--sn-surface, #1e293b);
        color: var(--sn-text, #f8fafc);
        border: 1px solid var(--sn-border, #334155);
        border-radius: 10px;
        box-shadow: 0 18px 48px rgba(0, 0, 0, 0.45);
        z-index: 9001;
        padding: 0 0 1rem;
      }
      .hdr {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0.85rem 1rem;
        border-bottom: 1px solid var(--sn-border, #334155);
      }
      .hdr h2 {
        margin: 0;
        font-size: 1.05rem;
      }
      .icon-close {
        font-size: 1.5rem;
        line-height: 1;
        padding: 0 0.35rem;
        border: none;
        background: transparent;
        color: inherit;
        cursor: pointer;
        opacity: 0.85;
      }
      .icon-close:hover {
        opacity: 1;
      }
      .headline {
        margin: 0.75rem 1rem 0;
        font-weight: 600;
        color: var(--sn-error, #f87171);
        word-break: break-word;
      }
      .actions {
        display: flex;
        gap: 0.65rem;
        flex-wrap: wrap;
        padding: 0.65rem 1rem;
      }
      .actions button {
        cursor: pointer;
        padding: 0.35rem 0.75rem;
        border-radius: 6px;
        border: 1px solid var(--sn-border, #334155);
        background: var(--sn-accent, #3b82f6);
        color: #fff;
      }
      .actions button.secondary {
        background: transparent;
        color: var(--sn-text, #f8fafc);
      }
      .lbl {
        margin: 0 1rem 0.35rem;
        font-size: 0.82rem;
        opacity: 0.85;
      }
      .body {
        margin: 0 1rem;
        flex: 1;
        min-height: 120px;
        overflow: auto;
        padding: 0.65rem 0.75rem;
        border-radius: 6px;
        background: rgba(0, 0, 0, 0.28);
        border: 1px solid var(--sn-border, #334155);
        font-size: 0.78rem;
        line-height: 1.35;
        white-space: pre-wrap;
        word-break: break-word;
      }
    `,
  ],
})
export class ApiErrorModalComponent {
  protected readonly svc = inject(ApiErrorReportService);
  protected readonly copyHint = signal('Copy full report');

  close(): void {
    this.copyHint.set('Copy full report');
    this.svc.dismiss();
  }

  onBackdrop(ev: MouseEvent): void {
    if (ev.target === ev.currentTarget) this.close();
  }

  async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copyHint.set('Copied!');
      window.setTimeout(() => this.copyHint.set('Copy full report'), 2200);
    } catch {
      this.copyHint.set('Copy blocked — select text manually');
      window.setTimeout(() => this.copyHint.set('Copy full report'), 3200);
    }
  }

  @HostListener('document:keydown', ['$event'])
  onDocKey(ev: KeyboardEvent): void {
    if (ev.key === 'Escape' && this.svc.dialog()) this.close();
  }
}
