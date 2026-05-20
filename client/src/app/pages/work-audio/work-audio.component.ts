import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { WorksApiService, AudioArtifactVm } from '../../core/works-api.service';

@Component({
  selector: 'app-work-audio',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h2>Audio queue</h2>
    <p class="hint">Each clip aligns with narration/dialogue spans. Render via the Analysis tab.</p>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    } @else if (clips().length === 0) {
      <p>No clips yet.</p>
    } @else {
      <ul>
        @for (a of clips(); track a.id) {
          <li>
            <a [href]="url(a)" target="_blank" rel="noreferrer">{{ a.relativePath }}</a>
            @if (a.startOffset !== null && a.endOffset !== null) {
              <span class="meta">{{ a.startOffset }}–{{ a.endOffset }}</span>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: [
    `
      .hint {
        opacity: 0.85;
      }
      .meta {
        margin-left: 0.5rem;
        font-size: 0.85rem;
        opacity: 0.65;
      }
      .error {
        color: var(--sn-error);
      }
      ul {
        list-style: none;
        padding: 0;
      }
      li {
        padding: 0.35rem 0;
      }
    `,
  ],
})
export class WorkAudioComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(WorksApiService);
  protected readonly clips = signal<AudioArtifactVm[]>([]);
  protected readonly error = signal<string | null>(null);

  private readonly workId =
    this.route.parent?.snapshot.paramMap.get('id') ??
    this.route.snapshot.paramMap.get('id')!;

  constructor() {
    this.api.listArtifacts(this.workId).subscribe({
      next: (items) => this.clips.set(items),
      error: (err) => this.error.set(err.message ?? 'Failed audio list'),
    });
  }

  url(a: AudioArtifactVm): string {
    return this.api.artifactDownloadUrl(this.workId, a.id);
  }
}
