import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { WorksApiService } from '../../core/works-api.service';

@Component({
  selector: 'app-work-profiles',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h2>Profile import / export</h2>
    @if (characterCount() !== null) {
      <p class="hint">
        Saved character profiles for this work: <strong>{{ characterCount() }}</strong>. Edit voices under
        <strong>Characters</strong>; narrator spans live under <strong>Summary</strong>. Download bundle exports characters plus narrator-passage metadata for backup/sharing — run <strong>AI analysis</strong> first so rows exist.
      </p>
    } @else {
      <p class="hint">Bundle character + narrator-passage metadata as JSON (export includes Summary narrator segments). Run AI analysis first.</p>
    }
    <div class="row">
      <button type="button" (click)="exportJson()">Download bundle</button>
    </div>
    <div class="row">
      <input type="file" accept="application/json,.json" (change)="onFile($event)" />
      <button type="button" [disabled]="!file" (click)="importJson()">Import bundle</button>
    </div>
    @if (note()) {
      <p class="banner">{{ note() }}</p>
    }
  `,
  styles: [
    `
      .hint {
        opacity: 0.85;
        max-width: 40rem;
      }
      .row {
        margin: 0.75rem 0;
      }
      .banner {
        padding: 0.5rem 0.75rem;
        background: var(--sn-banner-bg);
        border-left: 4px solid var(--sn-banner-border);
        color: var(--sn-text);
      }
    `,
  ],
})
export class WorkProfilesComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(WorksApiService);
  protected readonly note = signal<string | null>(null);
  protected readonly characterCount = signal<number | null>(null);
  protected file: File | null = null;

  private readonly workId =
    this.route.parent?.snapshot.paramMap.get('id') ??
    this.route.snapshot.paramMap.get('id')!;

  constructor() {
    this.api.characters(this.workId).subscribe({
      next: (list) => this.characterCount.set(list.length),
      error: () => this.characterCount.set(null),
    });
  }

  exportJson(): void {
    this.note.set(null);
    this.api.exportProfiles(this.workId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `smartnarrator-${this.workId}.json`;
        a.click();
        URL.revokeObjectURL(url);
        this.note.set('Export started.');
      },
      error: () => this.note.set('Export failed.'),
    });
  }

  onFile(evt: Event): void {
    const input = evt.target as HTMLInputElement;
    this.file = input.files?.[0] ?? null;
  }

  importJson(): void {
    if (!this.file) return;
    this.note.set(null);
    this.api.importProfiles(this.workId, this.file).subscribe({
      next: () => {
        this.note.set('Import complete.');
        this.api.characters(this.workId).subscribe({
          next: (list) => this.characterCount.set(list.length),
        });
      },
      error: () => this.note.set('Import failed.'),
    });
  }
}
