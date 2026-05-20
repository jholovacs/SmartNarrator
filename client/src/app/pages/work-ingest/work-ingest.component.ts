import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { WorksApiService, JobVm } from '../../core/works-api.service';
import { JobsHubConnectionService } from '../../core/jobs-hub.connection';

@Component({
  selector: 'app-work-ingest',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h2>Analysis</h2>
    <p class="hint">
      Queue <strong>AI analysis</strong> or <strong>speech rendering</strong> for this work. Initial text import happens on the
      <strong>Works</strong> page; ingest builds <strong>canonical Markdown</strong> and chapter boundaries before you land here.
      <strong>Analyze</strong> streams dialogue + character phases over SignalR while Ollama runs. While a job is
      <strong>pending</strong> or <strong>running</strong>, use <strong>Cancel job</strong> to stop it (running jobs stop
      cooperatively after the current HTTP step).
    </p>

    <section class="panel actions">
      <button type="button" [disabled]="busy()" (click)="analyze()">Queue AI analysis</button>
      <button type="button" [disabled]="busy()" (click)="render()">Queue speech render</button>
    </section>

    @if (job(); as j) {
      <section class="panel job">
        <h3>Latest job</h3>
        @if (showProgressBar(j)) {
          <div class="progress-wrap" aria-live="polite">
            <progress class="job-progress" max="100" [value]="clampPercent(j.progressPercent)"></progress>
            <span class="progress-label">{{ clampPercent(j.progressPercent) }}%</span>
          </div>
        }
        <p class="job-meta">
          <strong>{{ j.status }}</strong>
          @if (!showProgressBar(j)) {
            <span> — {{ j.progressPercent }}%</span>
          }
          <span> — </span>
          <span class="mono">{{ j.type }}</span>
        </p>
        @if (canCancelJob(j)) {
          <div class="cancel-row">
            <button type="button" class="cancel-job" [disabled]="cancelBusy() || j.cancellationRequested" (click)="cancelJob()">
              Cancel job
            </button>
            @if (j.cancellationRequested && j.status.toLowerCase() === 'running') {
              <span class="cancel-hint">Stopping after current step…</span>
            }
          </div>
        }
        @if (j.progressPhase) {
          <p class="phase">{{ j.progressPhase }}</p>
        }
        @if (j.errorMessage) {
          <p class="error">{{ j.errorMessage }}</p>
        }
      </section>
    }

    @if (message()) {
      <p class="banner">{{ message() }}</p>
    }
  `,
  styles: [
    `
      .panel {
        margin-bottom: 1.25rem;
      }
      button {
        margin-right: 1rem;
        margin-top: 0.35rem;
      }
      .hint {
        opacity: 0.9;
        max-width: 48rem;
      }
      .mono {
        font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      }
      .phase {
        margin-top: 0.35rem;
        opacity: 0.92;
        max-width: 52rem;
      }
      .error {
        color: var(--sn-error);
      }
      .banner {
        padding: 0.5rem 0.75rem;
        background: var(--sn-banner-bg);
        border-left: 4px solid var(--sn-banner-border);
        color: var(--sn-text);
      }
      .job {
        border: 1px solid var(--sn-border);
        padding: 0.75rem 1rem;
        border-radius: 6px;
        background: var(--sn-surface);
      }
      .progress-wrap {
        display: flex;
        align-items: center;
        gap: 0.65rem;
        margin-bottom: 0.65rem;
      }
      .job-progress {
        flex: 1;
        height: 0.65rem;
        accent-color: var(--sn-accent, #3b82f6);
      }
      .progress-label {
        font-size: 0.85rem;
        font-variant-numeric: tabular-nums;
        min-width: 2.75rem;
        text-align: right;
      }
      .cancel-row {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        flex-wrap: wrap;
        margin: 0.35rem 0 0.25rem;
      }
      button.cancel-job {
        margin-top: 0;
        border: 1px solid var(--sn-border);
        background: transparent;
        color: var(--sn-text);
      }
      button.cancel-job:hover:not(:disabled) {
        border-color: var(--sn-error);
        color: var(--sn-error);
      }
      button.cancel-job:disabled {
        opacity: 0.55;
      }
      .cancel-hint {
        font-size: 0.85rem;
        opacity: 0.88;
      }
    `,
  ],
})
export class WorkIngestComponent implements OnDestroy, OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(WorksApiService);
  private readonly jobsHub = inject(JobsHubConnectionService);
  private jobWatch?: Subscription;

  protected readonly busy = signal(false);
  protected readonly cancelBusy = signal(false);
  protected readonly job = signal<JobVm | null>(null);
  protected readonly message = signal<string | null>(null);

  /** Native progress bar while the job is pending or running (hidden once terminal). */
  protected showProgressBar(j: JobVm): boolean {
    const s = j.status.toLowerCase();
    return s === 'pending' || s === 'running';
  }

  protected clampPercent(n: number): number {
    if (!Number.isFinite(n)) return 0;
    return Math.min(100, Math.max(0, Math.round(n)));
  }

  protected canCancelJob(j: JobVm): boolean {
    const s = j.status.toLowerCase();
    return s === 'pending' || s === 'running';
  }

  cancelJob(): void {
    const j = this.job();
    if (!j || !this.canCancelJob(j)) return;

    this.cancelBusy.set(true);
    this.message.set(null);
    this.api
      .cancelJob(j.id)
      .pipe(finalize(() => this.cancelBusy.set(false)))
      .subscribe({
        error: () => undefined,
      });
  }

  private readonly workId =
    this.route.parent?.snapshot.paramMap.get('id') ??
    this.route.snapshot.paramMap.get('id')!;

  ngOnInit(): void {
    const jid =
      this.route.parent?.snapshot.queryParamMap.get('jobId') ??
      this.route.snapshot.queryParamMap.get('jobId');
    if (jid) this.watchJob(jid, 'Watching queued job.');
  }

  ngOnDestroy(): void {
    this.jobWatch?.unsubscribe();
  }

  analyze(): void {
    this.busy.set(true);
    this.message.set(null);
    this.api.analyze(this.workId).subscribe({
      next: (res) => this.watchJob(res.jobId, 'Analysis queued.'),
      error: () => this.busy.set(false),
    });
  }

  render(): void {
    this.busy.set(true);
    this.message.set(null);
    this.api.render(this.workId).subscribe({
      next: (res) => this.watchJob(res.jobId, 'Render queued.'),
      error: () => this.busy.set(false),
    });
  }

  private watchJob(jobId: string, note: string): void {
    this.message.set(note);
    this.jobWatch?.unsubscribe();

    const apply = (job: JobVm): void => {
      this.job.set(job);
      const s = job.status.toLowerCase();
      if (s !== 'pending' && s !== 'running') this.busy.set(false);
    };

    const snapshot = (): void => {
      this.api.job(jobId).subscribe({
        next: (job) => apply(job),
        error: () => this.busy.set(false),
      });
    };
    this.jobWatch = this.jobsHub.watchJob(jobId, apply, snapshot);
  }
}
