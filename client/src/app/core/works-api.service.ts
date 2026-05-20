import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface WorkSummary {
  id: string;
  title: string;
  language: string;
  createdUtc: string;
  canonicalTextLength: number;
  hasArtifacts: boolean;
}

export interface WorkDetail {
  id: string;
  title: string;
  language: string;
  createdUtc: string;
  canonicalText: string;
}

export interface StoryStructureSection {
  id: string;
  startOffset: number;
  endOffset: number;
  kind: string;
  title: string | null;
  notes: string;
  isAiSuggested: boolean;
}

export interface Timeline {
  canonicalText: string;
  segments: TextSegment[];
  utterances: Utterance[];
  narratives: NarrativePassage[];
  structureSections: StoryStructureSection[];
  workChapters: WorkChapter[];
  dialogueSpans: DialogueSpan[];
}

/** Matches POST .../timeline/bulk-merge | bulk-delete body (ASP.NET camelCase JSON). */
export type TimelineBulkEntityKind =
  | 'utterance'
  | 'narrative'
  | 'dialogueSpan'
  | 'workChapter'
  | 'structureSection';

export interface TimelineBulkRequest {
  entityKind: TimelineBulkEntityKind;
  ids: string[];
}

export interface WorkChapter {
  id: string;
  orderIndex: number;
  startOffset: number;
  endOffset: number;
  headingStartOffset: number | null;
  headingEndOffset: number | null;
  title: string | null;
  notes: string;
  isAiSuggested: boolean;
}

export interface DialogueSpan {
  id: string;
  chapterId: string;
  orderIndexInChapter: number;
  startOffset: number;
  endOffset: number;
  speakerKind: string;
  confidence: number;
  isAiSuggested: boolean;
}

export interface TextSegment {
  id: string;
  orderIndex: number;
  startOffset: number;
  endOffset: number;
}

export interface Utterance {
  id: string;
  startOffset: number;
  endOffset: number;
  speakerKind: string;
  characterId: string | null;
  confidence: number;
  speakerNeedsReview: boolean;
  isAiSuggested: boolean;
  userApproved: boolean;
}

export interface NarrativePassage {
  id: string;
  startOffset: number;
  endOffset: number;
  narratorCharacterId: string | null;
  perspectiveNotes: string;
  genderPresentation: string;
  tone: string;
  accent: string;
  breathiness: string;
  speakingPace: string;
  isAiSuggested: boolean;
}

export interface CharacterVm {
  id: string;
  aiExternalKey: string | null;
  name: string;
  aliases: string[] | null;
  personalitySummary: string | null;
  speechStyleSummary: string | null;
  genderPresentation: string;
  tone: string;
  accent: string;
  breathiness: string;
  speakingPace: string;
  isAiSuggested: boolean;
  userApproved: boolean;
}

export interface CharacterUpsert {
  id: string;
  name: string;
  aiExternalKey?: string | null;
  /** When true, AiExternalKey is applied (including clearing when null). Omit or false elsewhere so bulk saves do not wipe keys. */
  patchAiExternalKey?: boolean;
  aliases: string[] | null;
  personalitySummary?: string | null;
  speechStyleSummary?: string | null;
  genderPresentation: string;
  tone: string;
  accent: string;
  breathiness: string;
  speakingPace: string;
  userApproved: boolean;
}

export interface CharacterCreate {
  name: string;
}

export interface UtteranceUpsert {
  id: string;
  speakerKind: string;
  characterId: string | null;
  userApproved: boolean;
}

export interface NarrativePassageUpsert {
  id: string;
  narratorCharacterId: string | null;
  perspectiveNotes: string;
  genderPresentation: string;
  tone: string;
  accent: string;
  breathiness: string;
  speakingPace: string;
}

export interface JobVm {
  id: string;
  type: string;
  status: string;
  progressPercent: number;
  progressPhase: string | null;
  workId: string | null;
  payloadJson: string | null;
  errorMessage: string | null;
  /** Present after API supports cooperative cancellation (defaults falsy when absent). */
  cancellationRequested?: boolean;
  createdUtc: string;
  /** Last server-side mutation when supported by API (migration); falls back for display only. */
  updatedUtc?: string;
  startedUtc: string | null;
  completedUtc: string | null;
}

export interface AudioArtifactVm {
  id: string;
  relativePath: string;
  startOffset: number | null;
  endOffset: number | null;
  utteranceId: string | null;
}

/** Response from POST /works/import and POST /works/import-from-url */
export interface ImportWorkQueuedResponse {
  workId: string;
  jobId: string;
}

@Injectable({ providedIn: 'root' })
export class WorksApiService {
  private readonly http = inject(HttpClient);
  private readonly root = environment.apiBase;

  listWorks(): Observable<WorkSummary[]> {
    return this.http.get<WorkSummary[]>(`${this.root}/works`);
  }

  createWork(body: { title: string; language?: string }): Observable<WorkSummary> {
    return this.http.post<WorkSummary>(`${this.root}/works`, body);
  }

  deleteWork(workId: string): Observable<void> {
    return this.http.delete<void>(`${this.root}/works/${workId}`);
  }

  getWorkDetail(workId: string): Observable<WorkDetail> {
    return this.http.get<WorkDetail>(`${this.root}/works/${workId}/detail`);
  }

  ingest(workId: string, format: string, file: File): Observable<{ jobId: string }> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    const params = new HttpParams().set('format', format);
    return this.http.post<{ jobId: string }>(`${this.root}/works/${workId}/ingest`, fd, {
      params,
    });
  }

  /** Creates a work and queues ingestion from a local file. */
  importWorkFromDisk(params: {
    file: File;
    format: string;
    title?: string;
  }): Observable<ImportWorkQueuedResponse> {
    const fd = new FormData();
    fd.append('file', params.file, params.file.name);
    fd.append('format', params.format);
    if (params.title?.trim()) {
      fd.append('title', params.title.trim());
    }
    return this.http.post<ImportWorkQueuedResponse>(`${this.root}/works/import`, fd);
  }

  /** Creates a work and queues ingestion from a fetched HTTP(S) URL (handled server-side). */
  importWorkFromUrl(body: { url: string; format?: string | null; title?: string }): Observable<ImportWorkQueuedResponse> {
    const payload: { url: string; format?: string; title?: string } = {
      url: body.url.trim(),
    };
    const fmt = body.format?.trim();
    if (fmt) payload.format = fmt;
    if (body.title?.trim()) payload.title = body.title.trim();
    return this.http.post<ImportWorkQueuedResponse>(`${this.root}/works/import-from-url`, payload);
  }

  timeline(workId: string): Observable<Timeline> {
    return this.http.get<Timeline>(`${this.root}/works/${workId}/timeline`);
  }

  timelineBulkMerge(workId: string, body: TimelineBulkRequest): Observable<Timeline> {
    return this.http.post<Timeline>(`${this.root}/works/${workId}/timeline/bulk-merge`, body);
  }

  timelineBulkDelete(workId: string, body: TimelineBulkRequest): Observable<Timeline> {
    return this.http.post<Timeline>(`${this.root}/works/${workId}/timeline/bulk-delete`, body);
  }

  characters(workId: string): Observable<CharacterVm[]> {
    return this.http.get<CharacterVm[]>(`${this.root}/works/${workId}/characters`);
  }

  updateCharacters(workId: string, body: CharacterUpsert[]): Observable<CharacterVm[]> {
    return this.http.put<CharacterVm[]>(`${this.root}/works/${workId}/characters`, body);
  }

  createCharacter(workId: string, body: CharacterCreate): Observable<CharacterVm> {
    return this.http.post<CharacterVm>(`${this.root}/works/${workId}/characters`, body);
  }

  mergeCharacters(
    workId: string,
    body: { targetCharacterId: string; sourceCharacterIds: string[] },
  ): Observable<CharacterVm[]> {
    return this.http.post<CharacterVm[]>(`${this.root}/works/${workId}/characters/merge`, body);
  }

  deleteCharacter(workId: string, characterId: string): Observable<void> {
    return this.http.delete<void>(`${this.root}/works/${workId}/characters/${characterId}`);
  }

  utterances(workId: string): Observable<Utterance[]> {
    return this.http.get<Utterance[]>(`${this.root}/works/${workId}/utterances`);
  }

  updateUtterances(workId: string, body: UtteranceUpsert[]): Observable<Utterance[]> {
    return this.http.put<Utterance[]>(`${this.root}/works/${workId}/utterances`, body);
  }

  updateNarratives(workId: string, body: NarrativePassageUpsert[]): Observable<NarrativePassage[]> {
    return this.http.put<NarrativePassage[]>(`${this.root}/works/${workId}/narratives`, body);
  }

  analyze(workId: string): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>(`${this.root}/works/${workId}/analyze`, {});
  }

  /** Recent jobs (dashboard); polls recommended. */
  deleteJob(jobId: string, force = false): Observable<void> {
    let params = new HttpParams();
    if (force) params = params.set('force', 'true');
    return this.http.delete<void>(`${this.root}/jobs/${jobId}`, { params });
  }

  listRecentJobs(take = 150): Observable<JobVm[]> {
    const params = new HttpParams().set('take', String(take));
    return this.http.get<JobVm[]>(`${this.root}/jobs`, { params });
  }

  render(workId: string): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>(`${this.root}/works/${workId}/render`, {});
  }

  job(jobId: string): Observable<JobVm> {
    return this.http.get<JobVm>(`${this.root}/jobs/${jobId}`);
  }

  /** GET /jobs/{id}/llm-diagnostics JSON (captures prompts when Ollama:CaptureAnalyzeLlmTurns is true). */
  jobLlmDiagnosticsUrl(jobId: string): string {
    return `${this.root}/jobs/${jobId}/llm-diagnostics`;
  }

  cancelJob(jobId: string): Observable<void> {
    return this.http.post<void>(`${this.root}/jobs/${jobId}/cancel`, {});
  }

  listArtifacts(workId: string): Observable<AudioArtifactVm[]> {
    return this.http.get<AudioArtifactVm[]>(`${this.root}/works/${workId}/audio/artifacts`);
  }

  artifactDownloadUrl(workId: string, artifactId: string): string {
    return `${this.root}/works/${workId}/audio/artifacts/${artifactId}/file`;
  }

  exportProfiles(workId: string): Observable<Blob> {
    return this.http.get(`${this.root}/works/${workId}/profiles/export`, {
      responseType: 'blob',
    });
  }

  importProfiles(workId: string, file: File): Observable<void> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    return this.http.post<void>(`${this.root}/works/${workId}/profiles/import`, fd);
  }
}
