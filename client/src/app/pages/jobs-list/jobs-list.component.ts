import { CommonModule } from '@angular/common';
import { Component, inject, OnDestroy, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { JobVm, WorksApiService } from '../../core/works-api.service';
import { JobsHubConnectionService, normalizeJobId } from '../../core/jobs-hub.connection';

@Component({
  selector: 'app-jobs-list',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <h1>Background jobs</h1>

    <p class="hint">
      Jobs are queued in Postgres and dispatched via RabbitMQ in Docker (<strong>Analyze</strong> and
      <strong>Render speech</strong> share one AI queue with prefetch 1 so only one AI-heavy job runs at a time).
      Ingest jobs use a separate queue and may run in parallel.       Progress and phase update live over SignalR;
      the table still refreshes periodically over HTTP if a push is missed.
      <strong>LLM diag</strong> opens <code>GET /jobs/&lt;id&gt;/llm-diagnostics</code>
      (<code>Ollama:CaptureAnalyzeLlmTurns</code> on the executing host; older jobs respond 404 JSON with hints if nothing was captured).
      <strong>Stale</strong> is time since the server last persisted progress for that row (new jobs inherit created time until the first bump).
      Removing a row deletes it from Postgres; leftover RabbitMQ deliveries for that id do nothing because workers only run Pending rows.
      Use <strong>Remove</strong> to drop stale rows (failed / finished / stuck pending); running jobs need a
      <strong>force</strong> confirmation if the worker died (zombie).
    </p>

    @if (loadError()) {
      <p class="error">{{ loadError() }}</p>
    }

    <section class="panel">
      <table class="jobs-table">
        <thead>
          <tr>
            <th>Type</th>
            <th>AI</th>
            <th>Status</th>
            <th>%</th>
            <th>Phase</th>
            <th>Queued wait</th>
            <th>Running</th>
            <th>Stale</th>
            <th>Work</th>
            <th>LLM diag</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (j of jobs(); track j.id) {
            <tr>
              <td class="mono">{{ j.type }}</td>
              <td>{{ isAiJob(j) ? 'yes' : '—' }}</td>
              <td>{{ j.status }}</td>
              <td>{{ j.progressPercent }}</td>
              <td class="phase">{{ j.progressPhase ?? '—' }}</td>
              <td>{{ formatHm(queueDurationMs(j)) }}</td>
              <td>{{ formatHm(runningDurationMs(j)) }}</td>
              <td>{{ formatHm(stalenessMs(j)) }}</td>
              <td>
                @if (j.workId) {
                  <a [routerLink]="['/works', j.workId]">Open</a>
                } @else {
                  —
                }
              </td>
              <td>
                @if (llmDiagEligible(j)) {
                  <a [href]="llmDiagUrl(j.id)" target="_blank" rel="noopener noreferrer">Open</a>
                } @else {
                  —
                }
              </td>
              <td class="actions">
                <button type="button" class="btn-remove" [disabled]="deleteBusy()" (click)="removeJob(j)">Remove</button>
              </td>
            </tr>
          }
        </tbody>
      </table>
      @if (jobs().length === 0 && !loadError()) {
        <p class="hint">No jobs recorded yet.</p>
      }
    </section>
  `,
  styles: [
    `
      .hint {
        opacity: 0.82;
        font-size: 0.92rem;
        max-width: 52rem;
      }
      .error {
        color: #f87171;
      }
      .panel {
        margin-top: 1rem;
      }
      .jobs-table {
        border-collapse: collapse;
        width: 100%;
      }
      .jobs-table th,
      .jobs-table td {
        text-align: left;
        padding: 0.35rem 0.5rem;
        border-bottom: 1px solid var(--sn-border);
        vertical-align: top;
      }
      .mono {
        font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
        font-size: 0.82rem;
      }
      .phase {
        max-width: 22rem;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      .actions {
        white-space: nowrap;
      }
      button.btn-remove {
        padding: 0.15rem 0.45rem;
        font-size: 0.78rem;
        border-radius: 4px;
        border: 1px solid var(--sn-border);
        background: transparent;
        color: var(--sn-error, #f87171);
        cursor: pointer;
      }
      button.btn-remove:hover:not(:disabled) {
        border-color: var(--sn-error, #f87171);
      }
      button.btn-remove:disabled {
        opacity: 0.45;
        cursor: not-allowed;
      }
    `,
  ],
})
export class JobsListComponent implements OnDestroy {
  private readonly api = inject(WorksApiService);
  private readonly jobsHub = inject(JobsHubConnectionService);

  readonly jobs = signal<JobVm[]>([]);
  readonly loadError = signal<string | null>(null);
  readonly deleteBusy = signal(false);
  /** Drives relative “stale” labels without hammering HTTP. */
  readonly nowMs = signal(Date.now());

  private readonly hubSub: Subscription;
  private staleTicker?: ReturnType<typeof setInterval>;

  constructor() {
    this.staleTicker = setInterval(() => this.nowMs.set(Date.now()), 1000);
    this.hubSub = this.jobsHub.watchRecentJobsFeed(
      (j) => this.mergeJobRow(j),
      () => this.fetchJobsSnapshot(),
      { onRemoved: (nid) => this.removeJobRowByNormalizedId(nid) },
    );
  }

  ngOnDestroy(): void {
    if (this.staleTicker !== undefined) clearInterval(this.staleTicker);
    this.hubSub.unsubscribe();
  }

  private fetchJobsSnapshot(): void {
    this.api.listRecentJobs(200).subscribe({
      next: (rows) => {
        this.loadError.set(null);
        this.jobs.set(rows);
      },
      error: (err: unknown) =>
        this.loadError.set(err instanceof Error ? err.message : 'Failed to load jobs'),
    });
  }

  private mergeJobRow(updated: JobVm): void {
    const id = normalizeJobId(updated.id);
    const rows = this.jobs();
    const idx = rows.findIndex((r) => normalizeJobId(r.id) === id);
    let next: JobVm[];
    if (idx >= 0) {
      next = [...rows];
      next[idx] = updated;
    } else {
      next = [updated, ...rows];
    }
    next.sort((a, b) => Date.parse(b.createdUtc) - Date.parse(a.createdUtc));
    this.jobs.set(next.slice(0, 200));
  }

  private removeJobRowByNormalizedId(normalizedId: string): void {
    const rows = this.jobs().filter((r) => normalizeJobId(r.id) !== normalizedId);
    this.jobs.set(rows);
  }

  removeJob(j: JobVm): void {
    const running = j.status.toLowerCase() === 'running';
    const ok = running
      ? confirm(
          `FORCE-remove this RUNNING ${j.type} job?\nOnly if the worker crashed (zombie); an alive worker may still touch storage.`,
        )
      : confirm(`Remove this ${j.type} job from history?`);
    if (!ok) return;

    this.deleteBusy.set(true);
    this.loadError.set(null);
    this.api.deleteJob(j.id, running).subscribe({
      next: () => {
        this.removeJobRowByNormalizedId(normalizeJobId(j.id));
        this.deleteBusy.set(false);
      },
      error: (err: unknown) => {
        const msg =
          err && typeof err === 'object' && 'error' in err && typeof (err as { error?: unknown }).error === 'object'
            ? JSON.stringify((err as { error: unknown }).error)
            : err instanceof Error
              ? err.message
              : 'Remove failed';
        this.loadError.set(msg);
        this.deleteBusy.set(false);
      },
    });
  }

  isAiJob(job: JobVm): boolean {
    const t = (job.type ?? '').toLowerCase();
    return t === 'analyze' || t === 'renderspeech';
  }

  /** Analyze + ingest structured Ollama calls may emit llm-diagnostics when capture is enabled. */
  llmDiagEligible(job: JobVm): boolean {
    const t = (job.type ?? '').toLowerCase();
    return t === 'analyze' || t === 'ingest';
  }

  queueDurationMs(job: JobVm): number | null {
    const created = Date.parse(job.createdUtc);
    if (Number.isNaN(created)) return null;
    if (job.status.toLowerCase() === 'pending') return Math.max(0, this.nowMs() - created);
    if (!job.startedUtc) return null;
    const started = Date.parse(job.startedUtc);
    if (Number.isNaN(started)) return null;
    return Math.max(0, started - created);
  }

  runningDurationMs(job: JobVm): number | null {
    if (job.status.toLowerCase() !== 'running' || !job.startedUtc) return null;
    const started = Date.parse(job.startedUtc);
    if (Number.isNaN(started)) return null;
    return Math.max(0, this.nowMs() - started);
  }

  stalenessMs(job: JobVm): number | null {
    const raw = job.updatedUtc ?? job.completedUtc ?? job.createdUtc;
    const u = Date.parse(raw);
    if (Number.isNaN(u)) return null;
    return Math.max(0, this.nowMs() - u);
  }

  formatHm(ms: number | null): string {
    if (ms == null || !Number.isFinite(ms)) return '—';
    const sec = Math.floor(ms / 1000);
    if (sec < 60) return `${sec}s`;
    const min = Math.floor(sec / 60);
    const hr = Math.floor(min / 60);
    if (hr === 0) return `${min}m`;
    const day = Math.floor(hr / 24);
    if (day === 0) return `${hr}h ${min % 60}m`;
    return `${day}d`;
  }

  /** Same-origin URL for GET captured LLM turns (requires Ollama:CaptureAnalyzeLlmTurns for new jobs). */
  llmDiagUrl(jobId: string): string {
    return this.api.jobLlmDiagnosticsUrl(jobId);
  }
}
