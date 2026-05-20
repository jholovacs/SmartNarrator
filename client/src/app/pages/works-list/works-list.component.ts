import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import {
  IMPORT_SOURCE_FORMAT_OPTIONS,
  inferImportFormatFromFile,
  inferImportFormatFromUrl,
  STORY_FILE_ACCEPT,
  type ImportSourceFormat,
} from '../../core/import-formats';
import { JobsHubConnectionService } from '../../core/jobs-hub.connection';
import { WorksApiService, JobVm, WorkSummary } from '../../core/works-api.service';

@Component({
  selector: 'app-works-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="works-header">
      <div>
        <h1>Works</h1>
        <p class="hint">
          Open a work below to review its <strong>Summary</strong>, characters, and audio. Import adds a source from an
          HTTPS URL or a file on this device — format and title are filled in automatically when we can detect them.
          After import, we stay here until ingestion finishes successfully, then open <strong>Summary</strong>.
        </p>
      </div>
      <button type="button" class="import-trigger" (click)="openImportModal()">Import work</button>
    </div>

    <section class="library" aria-labelledby="works-library-heading">
      <h2 id="works-library-heading" class="library-heading">Your works</h2>
      @if (works().length === 0) {
        <p class="empty">No works yet. Use <strong>Import work</strong> to add one.</p>
      } @else {
        <ul class="work-list">
          @for (w of works(); track w.id) {
            <li>
              <a [routerLink]="['/works', w.id, 'timeline']">{{ w.title }}</a>
              <span class="meta">{{ w.canonicalTextLength }} chars@if (w.hasArtifacts) { · audio}</span>
              <button type="button" class="linkish" (click)="remove(w.id)">Delete</button>
            </li>
          }
        </ul>
      }
    </section>

    <dialog
      #importDialog
      class="import-modal"
      aria-labelledby="import-modal-title"
      (close)="onImportDialogDismiss()"
    >
      <form method="dialog" class="import-dialog-inner" (submit)="$event.preventDefault()">
        <h3 id="import-modal-title">Import a work</h3>
        <p class="dialog-hint">
          Choose a local file or paste an absolute <strong>https://</strong> location. Title and format are inferred
          from the file name, URL path, or HTML page title when possible.
        </p>

        @if (errorMessage()) {
          <p class="error-banner" role="alert">{{ errorMessage() }}</p>
        }

        <label class="block"
          >Location
          <input
            name="locationUrl"
            type="url"
            autocomplete="off"
            spellcheck="false"
            placeholder="https://example.com/story.html"
            [(ngModel)]="locationUrl"
            (ngModelChange)="onLocationUrlChange()"
          />
        </label>

        <div class="file-row">
          <input
            #fileInput
            type="file"
            class="visually-hidden"
            tabindex="-1"
            [accept]="storyFileAccept"
            (change)="onDiskFile($event)"
          />
          <button type="button" class="secondary" (click)="fileInput.click()">Choose file…</button>
          @if (pickedFile()) {
            <span class="picked-label">Selected: {{ pickedFile()!.name }}</span>
          }
        </div>

        <label class="block"
          >Format <span class="format-hint">(from extension when recognized)</span>
          <select [value]="format()" (change)="onFormatChange($event)">
            @for (opt of importFormatOptions; track opt.value) {
              <option [value]="opt.value">{{ opt.label }}</option>
            }
          </select>
        </label>

        <label class="block"
          >Title (optional)
          <input [(ngModel)]="optionalTitle" name="importTitle" placeholder="Uses file name or page title when empty" />
        </label>

        <div class="dialog-actions">
          <button type="button" class="secondary" [disabled]="importBusy()" (click)="closeImportModal()">
            Cancel
          </button>
          <button type="button" [disabled]="!canSubmitImport() || importBusy()" (click)="submitImport()">
            @if (importBusy()) {
              <span>Importing…</span>
            } @else {
              <span>Import</span>
            }
          </button>
        </div>
      </form>
    </dialog>
  `,
  styles: [
    `
      .works-header {
        display: flex;
        flex-wrap: wrap;
        align-items: flex-start;
        justify-content: space-between;
        gap: 1rem;
        margin-bottom: 1.25rem;
      }
      .works-header h1 {
        margin: 0 0 0.35rem;
      }
      .hint {
        opacity: 0.85;
        max-width: 44rem;
        margin: 0;
      }
      .import-trigger {
        flex-shrink: 0;
        padding: 0.45rem 1rem;
        border-radius: 999px;
        font-weight: 600;
      }
      .library-heading {
        font-size: 1rem;
        margin: 0 0 0.65rem;
        font-weight: 600;
      }
      .empty {
        opacity: 0.85;
      }
      .work-list {
        list-style: none;
        padding: 0;
        margin: 0;
      }
      .work-list li {
        margin-bottom: 0.5rem;
      }
      .block {
        display: block;
        margin-bottom: 0.65rem;
      }
      .block input,
      .block select {
        margin-top: 0.25rem;
        min-width: min(36rem, 100%);
      }
      button {
        margin-right: 0.75rem;
      }
      .meta {
        opacity: 0.7;
        margin-left: 0.5rem;
        font-size: 0.85rem;
      }
      .linkish {
        margin-left: 0.75rem;
        background: none;
        border: none;
        color: var(--sn-link-alt);
        cursor: pointer;
        text-decoration: underline;
      }
      .format-hint {
        font-weight: normal;
        opacity: 0.75;
        font-size: 0.92em;
      }
      .import-modal {
        border: 1px solid color-mix(in srgb, CanvasText 18%, Canvas);
        border-radius: 10px;
        padding: 0;
        max-width: min(40rem, 94vw);
        background: Canvas;
        color: CanvasText;
      }
      .import-modal::backdrop {
        background: rgba(0, 0, 0, 0.38);
      }
      .import-dialog-inner {
        padding: 1.25rem 1.35rem;
      }
      .import-dialog-inner h3 {
        margin: 0 0 0.5rem;
      }
      .dialog-hint {
        opacity: 0.85;
        margin: 0 0 1rem;
        font-size: 0.92rem;
      }
      .error-banner {
        margin: 0 0 0.85rem;
        padding: 0.5rem 0.65rem;
        border-radius: 6px;
        background: color-mix(in srgb, firebrick 14%, Canvas);
      }
      .file-row {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 0.65rem;
        margin-bottom: 0.65rem;
      }
      .picked-label {
        font-size: 0.9rem;
        opacity: 0.85;
      }
      .dialog-actions {
        margin-top: 1rem;
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
      }
      .dialog-actions button {
        margin-right: 0;
      }
      .secondary {
        opacity: 0.95;
      }
      .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
    `,
  ],
})
export class WorksListComponent implements OnInit, OnDestroy {
  @ViewChild('importDialog') private importDialog?: ElementRef<HTMLDialogElement>;

  private readonly api = inject(WorksApiService);
  private readonly router = inject(Router);
  private readonly jobsHub = inject(JobsHubConnectionService);

  private ingestJobWatch?: Subscription;

  protected readonly works = signal<WorkSummary[]>([]);
  protected readonly importFormatOptions = IMPORT_SOURCE_FORMAT_OPTIONS;
  protected readonly storyFileAccept = STORY_FILE_ACCEPT;
  protected readonly format = signal<ImportSourceFormat>('PlainText');

  protected locationUrl = '';
  protected readonly pickedFile = signal<File | null>(null);
  protected optionalTitle = '';

  protected readonly importBusy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.refresh();
  }

  ngOnDestroy(): void {
    this.ingestJobWatch?.unsubscribe();
  }

  refresh(): void {
    this.api.listWorks().subscribe((w) => this.works.set(w));
  }

  protected openImportModal(): void {
    this.resetImportForm();
    queueMicrotask(() => this.importDialog?.nativeElement.showModal());
  }

  protected closeImportModal(): void {
    this.importDialog?.nativeElement.close();
  }

  protected onImportDialogDismiss(): void {
    this.resetImportForm();
  }

  private resetImportForm(): void {
    this.ingestJobWatch?.unsubscribe();
    this.ingestJobWatch = undefined;
    this.locationUrl = '';
    this.pickedFile.set(null);
    this.optionalTitle = '';
    this.format.set('PlainText');
    this.importBusy.set(false);
    this.errorMessage.set(null);
  }

  protected onLocationUrlChange(): void {
    if (this.locationUrl.trim().length > 0) this.pickedFile.set(null);
    const inferred = inferImportFormatFromUrl(this.locationUrl);
    if (inferred !== null) this.format.set(inferred);
  }

  protected onDiskFile(evt: Event): void {
    const input = evt.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.pickedFile.set(file);
    if (file) {
      this.locationUrl = '';
      const inferred = inferImportFormatFromFile(file);
      if (inferred !== null) this.format.set(inferred);
    }
    input.value = '';
  }

  protected onFormatChange(evt: Event): void {
    const v = (evt.target as HTMLSelectElement).value as ImportSourceFormat;
    this.format.set(v);
  }

  protected canSubmitImport(): boolean {
    return !!(this.pickedFile() || this.isValidHttpLocation(this.locationUrl));
  }

  private isValidHttpLocation(s: string): boolean {
    const t = s.trim();
    if (!t) return false;
    try {
      const u = new URL(t);
      return u.protocol === 'http:' || u.protocol === 'https:';
    } catch {
      return false;
    }
  }

  protected submitImport(): void {
    if (!this.canSubmitImport() || this.importBusy()) return;
    this.importBusy.set(true);
    this.errorMessage.set(null);

    const title = this.optionalTitle.trim();
    const titleOpt = title ? { title } : {};

    const file = this.pickedFile();
    if (file) {
      this.api
        .importWorkFromDisk({
          file,
          format: this.format(),
          ...titleOpt,
        })
        .subscribe({
          next: (res) => this.afterImportQueued(res),
          error: (err) => this.onImportHttpError(err),
        });
      return;
    }

    this.api
      .importWorkFromUrl({
        url: this.locationUrl.trim(),
        format: this.format(),
        ...titleOpt,
      })
      .subscribe({
        next: (res) => this.afterImportQueued(res),
        error: (err) => this.onImportHttpError(err),
      });
  }

  private afterImportQueued(res: { workId: string; jobId: string }): void {
    this.ingestJobWatch?.unsubscribe();

    let settled = false;

    const finishSuccess = (): void => {
      if (settled) return;
      settled = true;
      this.ingestJobWatch?.unsubscribe();
      this.ingestJobWatch = undefined;
      void this.router.navigate(['/works', res.workId, 'timeline']).then(() => {
        this.refresh();
        this.importBusy.set(false);
        this.closeImportModal();
      });
    };

    const fail = (msg: string): void => {
      if (settled) return;
      settled = true;
      this.ingestJobWatch?.unsubscribe();
      this.ingestJobWatch = undefined;
      this.importBusy.set(false);
      this.errorMessage.set(msg);
    };

    const apply = (job: JobVm): void => {
      const s = job.status.toLowerCase();
      if (s === 'succeeded') finishSuccess();
      else if (s === 'failed' || s === 'cancelled') {
        const phase = job.progressPhase?.trim();
        const err = job.errorMessage?.trim();
        const tail = [phase, err].filter(Boolean).join(' — ');
        fail(tail.length > 0 ? tail : `Import ended as ${job.status}.`);
      }
    };

    const snapshot = (): void => {
      this.api.job(res.jobId).subscribe({
        next: (job) => apply(job),
        error: () => fail('Could not load import job status (it may have been deleted).'),
      });
    };

    this.ingestJobWatch = this.jobsHub.watchJob(res.jobId, apply, snapshot);
  }

  private onImportHttpError(err: unknown): void {
    this.importBusy.set(false);
    let msg = 'Import failed. Check the URL or file and try again.';
    if (typeof err === 'object' && err !== null && 'error' in err) {
      const body = (err as { error?: unknown }).error;
      if (typeof body === 'string' && body.trim()) msg = body.trim();
      else if (typeof body === 'object' && body !== null) {
        const o = body as { detail?: unknown; Detail?: unknown };
        const d = o.detail ?? o.Detail;
        if (typeof d === 'string' && d.trim()) msg = d.trim();
      }
    }
    this.errorMessage.set(msg);
  }

  remove(id: string): void {
    if (!confirm('Delete this work and all derived data?')) return;
    this.api.deleteWork(id).subscribe(() => this.refresh());
  }
}
