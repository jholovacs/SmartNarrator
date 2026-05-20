import { Injectable, NgZone } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subscription, timer } from 'rxjs';
import { environment } from '../../environments/environment';
import type { JobVm } from './works-api.service';

/** Must match SmartNarrator.Api.Hubs.JobsHub.JobUpdateEvent */
export const JOB_HUB_JOB_UPDATE_EVENT = 'jobUpdate';

/** Must match SmartNarrator.Api.Hubs.JobsHub.JobRemovedEvent */
export const JOB_HUB_JOB_REMOVED_EVENT = 'jobRemoved';

/** Normalize GUID strings for comparisons (matches hub group ids). */
export function normalizeJobId(raw: unknown): string {
  return String(raw ?? '')
    .replace(/-/g, '')
    .toLowerCase();
}

function extractJobIdFromPayload(payload: unknown): string | null {
  if (payload != null && typeof payload === 'object' && 'id' in payload) {
    const v = (payload as { id?: unknown }).id;
    if (typeof v === 'string') return v;
  }
  if (typeof payload === 'string') return payload;
  return null;
}

function resolveHubAbsoluteUrl(): string {
  const pathOrUrl = environment.jobsHubUrl;
  return pathOrUrl.startsWith('http') ? pathOrUrl : `${window.location.origin}${pathOrUrl}`;
}

/**
 * Singleton SignalR hub for `/hubs/jobs`, reached from the SPA as `/api/hubs/jobs`.
 */
@Injectable({ providedIn: 'root' })
export class JobsHubConnectionService {
  private connection: HubConnection | null = null;
  private startPromise: Promise<void> | null = null;

  /** Jobs the UI asked to watch (re-invoked after automatic reconnect). */
  private readonly watchedJobIds = new Set<string>();

  /** Connections subscribed via {@link watchRecentJobsFeed} — rejoined after reconnect. */
  private recentJobsFeedRefs = 0;

  constructor(private readonly zone: NgZone) {}

  /**
   * Live updates plus HTTP fallback polling (`timer(1000, 1000)`): catches missed SignalR pushes without waiting 12s between polls.
   */
  watchJob(
    jobId: string,
    onUpdate: (job: JobVm) => void,
    fetchSnapshot: () => void,
    options?: { log?: boolean },
  ): Subscription {
    const targetId = normalizeJobId(jobId);
    let hubCallback: ((j: JobVm) => void) | null = null;
    let activeConnForTeardown: HubConnection | null = null;

    const applyUpdate = (job: JobVm) => {
      if (normalizeJobId(job.id) !== targetId) return;
      this.zone.run(() => onUpdate(job));
    };

    // `interval(n)` waits n ms before the *first* emit — so we previously polled only once at subscribe + once per 12s,
    // letting fast jobs jump from 0% → 100% when SignalR missed an update.
    fetchSnapshot();
    const pollSub = timer(1000, 1000).subscribe(() => fetchSnapshot());

    void this.ensureStarted(options?.log)
      .then(async (conn) => {
        activeConnForTeardown = conn;
        hubCallback = (payload: JobVm) => applyUpdate(payload);
        conn.on(JOB_HUB_JOB_UPDATE_EVENT, hubCallback);
        await conn.invoke('WatchJob', jobId);
        this.watchedJobIds.add(jobId);
      })
      .catch(() => fetchSnapshot());

    return new Subscription(() => {
      pollSub.unsubscribe();
      if (activeConnForTeardown && hubCallback)
        activeConnForTeardown.off(JOB_HUB_JOB_UPDATE_EVENT, hubCallback);
      this.watchedJobIds.delete(jobId);
      if (activeConnForTeardown) {
        void activeConnForTeardown.invoke('UnwatchJob', jobId).catch(() => undefined);
      }
    });
  }

  /**
   * Jobs dashboard: receive every job update over SignalR plus infrequent HTTP refresh if pushes are missed.
   */
  watchRecentJobsFeed(
    onUpdate: (job: JobVm) => void,
    fetchSnapshot: () => void,
    options?: { onRemoved?: (normalizedJobId: string) => void },
  ): Subscription {
    let hubCallback: ((j: JobVm) => void) | null = null;
    let removedCallback: ((payload: unknown) => void) | null = null;
    let activeConnForTeardown: HubConnection | null = null;

    const applyUpdate = (job: JobVm) => {
      this.zone.run(() => onUpdate(job));
    };

    fetchSnapshot();
    const pollSub = timer(4000, 20000).subscribe(() => fetchSnapshot());

    this.recentJobsFeedRefs++;
    void this.ensureStarted()
      .then(async (conn) => {
        activeConnForTeardown = conn;
        hubCallback = (payload: JobVm) => applyUpdate(payload);
        conn.on(JOB_HUB_JOB_UPDATE_EVENT, hubCallback);
        if (options?.onRemoved) {
          const onRm = options.onRemoved;
          removedCallback = (payload: unknown) => {
            const rawId = extractJobIdFromPayload(payload);
            if (!rawId) return;
            this.zone.run(() => onRm(normalizeJobId(rawId)));
          };
          conn.on(JOB_HUB_JOB_REMOVED_EVENT, removedCallback);
        }
        await conn.invoke('WatchRecentJobs');
      })
      .catch(() => fetchSnapshot());

    return new Subscription(() => {
      pollSub.unsubscribe();
      if (activeConnForTeardown && hubCallback)
        activeConnForTeardown.off(JOB_HUB_JOB_UPDATE_EVENT, hubCallback);
      if (activeConnForTeardown && removedCallback)
        activeConnForTeardown.off(JOB_HUB_JOB_REMOVED_EVENT, removedCallback);
      this.recentJobsFeedRefs--;
      if (activeConnForTeardown && this.recentJobsFeedRefs <= 0) {
        void activeConnForTeardown.invoke('UnwatchRecentJobs').catch(() => undefined);
      }
    });
  }

  private async ensureStarted(logVerbose = false): Promise<HubConnection> {
    const url = resolveHubAbsoluteUrl();

    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl(url, { withCredentials: true })
        .withAutomaticReconnect([0, 1000, 3000, 8000])
        .configureLogging(logVerbose ? LogLevel.Information : LogLevel.Warning)
        .build();

      this.connection.onreconnected(async () => {
        for (const id of this.watchedJobIds) {
          try {
            await this.connection?.invoke('WatchJob', id);
          } catch {
            /* ignore */
          }
        }
        if (this.recentJobsFeedRefs > 0) {
          try {
            await this.connection?.invoke('WatchRecentJobs');
          } catch {
            /* ignore */
          }
        }
      });
    }

    if (this.connection.state === HubConnectionState.Connected) {
      return this.connection;
    }

    if (!this.startPromise) {
      this.startPromise = this.connection
        .start()
        .catch((err) => {
          this.startPromise = null;
          throw err;
        })
        .then(() => undefined);
    }

    await this.startPromise;

    const conn = this.connection;
    if (!conn || conn.state !== HubConnectionState.Connected) {
      throw new Error('SignalR hub not connected.');
    }

    return conn;
  }
}
