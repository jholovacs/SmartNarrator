import { CommonModule } from '@angular/common';
import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { marked } from 'marked';
import {
  WorksApiService,
  CharacterUpsert,
  CharacterVm,
  DialogueSpan,
  NarrativePassage,
  NarrativePassageUpsert,
  StoryStructureSection,
  Timeline,
  TimelineBulkEntityKind,
  Utterance,
  UtteranceUpsert,
  WorkChapter,
} from '../../core/works-api.service';

type TimelineModal =
  | { kind: 'utterance'; utterance: Utterance }
  | { kind: 'dialogueSpan'; span: DialogueSpan }
  | { kind: 'chapter'; chapter: WorkChapter }
  | { kind: 'structure'; section: StoryStructureSection }
  | { kind: 'narrative'; narrative: NarrativePassage };

type TimelineSelection = Record<TimelineBulkEntityKind, Set<string>>;
@Component({
  selector: 'app-work-timeline',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <h2>Summary</h2>
    <p class="hint">
      Canonical story text with labeled utterances, narrator passages, dialogue spans, chapters, and structural sections.
      <strong>Click the row text</strong> for excerpt preview (raw Markdown vs formatted) and voice controls where they
      apply. Use <strong>checkboxes</strong> to multi-select rows in a column, then <strong>Merge</strong> (union UTF-16
      spans; earliest row keeps IDs/metadata rules below) or <strong>Delete</strong>.
    </p>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    } @else {
      @if (bulkOpError()) {
        <p class="bulk-op-error">{{ bulkOpError() }}</p>
      }
      @if (data(); as t) {
        <section class="stats">
          <span>{{ t.canonicalText.length.toLocaleString() }} characters</span>
          <span>{{ t.utterances.length }} utterances</span>
          <span>{{ t.narratives.length }} narrator segments</span>
          <span>{{ t.structureSections.length }} structural sections</span>
          <span>{{ t.workChapters.length }} chapters</span>
          <span>{{ t.dialogueSpans.length }} dialogue spans</span>
        </section>
        <article class="prose">
          <pre>{{ t.canonicalText }}</pre>
        </article>
        <section class="lists">
          <div>
            <h3>Utterances</h3>
            @if (selection().utterance.size > 0) {
              <div class="bulk-bar">
                <span>{{ selection().utterance.size }} selected</span>
                <button
                  type="button"
                  [disabled]="timelineBulkBusy() || selection().utterance.size < 2"
                  (click)="mergeSelected('utterance')"
                >
                  Merge
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="deleteSelected('utterance')">
                  Delete
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="clearSelection('utterance')">
                  Clear
                </button>
              </div>
            }
            <ul>
              @for (u of t.utterances; track u.id) {
                <li class="click-row">
                  <label class="row-check" (click)="$event.stopPropagation()">
                    <input
                      type="checkbox"
                      [checked]="isRowSelected('utterance', u.id)"
                      (change)="toggleRow('utterance', u.id, $event)"
                    />
                  </label>
                  <span
                    class="row-main"
                    tabindex="0"
                    (click)="openUtterance(u)"
                    (keydown.enter)="openUtterance(u)"
                  >
                    <span
                      class="sk"
                      [class.ai]="u.isAiSuggested"
                      [class.nonspeech]="normalizeUtteranceKind(u.speakerKind) === 'quotedNonSpeech'"
                      >{{ u.speakerKind }}</span
                    >
                    {{ u.startOffset }}–{{ u.endOffset }} · conf {{ u.confidence | number: '1.2-2' }}
                    @if (u.isAiSuggested) {
                      <span class="badge">AI</span>
                    }
                  </span>
                </li>
              }
            </ul>
          </div>
          <div>
            <h3>Chapters</h3>
            @if (selection().workChapter.size > 0) {
              <div class="bulk-bar">
                <span>{{ selection().workChapter.size }} selected</span>
                <button
                  type="button"
                  [disabled]="timelineBulkBusy() || selection().workChapter.size < 2"
                  (click)="mergeSelected('workChapter')"
                >
                  Merge
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="deleteSelected('workChapter')">
                  Delete
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="clearSelection('workChapter')">
                  Clear
                </button>
              </div>
            }
            <ul>
              @for (c of t.workChapters; track c.id) {
                <li class="click-row">
                  <label class="row-check" (click)="$event.stopPropagation()">
                    <input
                      type="checkbox"
                      [checked]="isRowSelected('workChapter', c.id)"
                      (change)="toggleRow('workChapter', c.id, $event)"
                    />
                  </label>
                  <span class="row-main" tabindex="0" (click)="openChapter(c)" (keydown.enter)="openChapter(c)">
                    <strong>#{{ c.orderIndex + 1 }}</strong>
                    {{ c.startOffset }}–{{ c.endOffset }}
                    @if (c.title) {
                      · {{ c.title }}
                    }
                    @if (c.isAiSuggested) {
                      <span class="badge">AI</span>
                    }
                  </span>
                </li>
              }
            </ul>
          </div>
          <div>
            <h3>Dialogue spans</h3>
            @if (selection().dialogueSpan.size > 0) {
              <div class="bulk-bar">
                <span>{{ selection().dialogueSpan.size }} selected</span>
                <button
                  type="button"
                  [disabled]="timelineBulkBusy() || selection().dialogueSpan.size < 2"
                  (click)="mergeSelected('dialogueSpan')"
                >
                  Merge
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="deleteSelected('dialogueSpan')">
                  Delete
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="clearSelection('dialogueSpan')">
                  Clear
                </button>
              </div>
            }
            <ul>
              @for (d of t.dialogueSpans; track d.id) {
                <li class="click-row">
                  <label class="row-check" (click)="$event.stopPropagation()">
                    <input
                      type="checkbox"
                      [checked]="isRowSelected('dialogueSpan', d.id)"
                      (change)="toggleRow('dialogueSpan', d.id, $event)"
                    />
                  </label>
                  <span
                    class="row-main"
                    tabindex="0"
                    (click)="openDialogueSpan(d)"
                    (keydown.enter)="openDialogueSpan(d)"
                  >
                    <span [class.ai]="d.isAiSuggested">{{ d.speakerKind }}</span>
                    ch {{ d.orderIndexInChapter }} · {{ d.startOffset }}–{{ d.endOffset }}
                    · conf {{ d.confidence | number: '1.2-2' }}
                    @if (d.isAiSuggested) {
                      <span class="badge">AI</span>
                    }
                  </span>
                </li>
              }
            </ul>
          </div>
          <div>
            <h3>Structural sections</h3>
            @if (selection().structureSection.size > 0) {
              <div class="bulk-bar">
                <span>{{ selection().structureSection.size }} selected</span>
                <button
                  type="button"
                  [disabled]="timelineBulkBusy() || selection().structureSection.size < 2"
                  (click)="mergeSelected('structureSection')"
                >
                  Merge
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="deleteSelected('structureSection')">
                  Delete
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="clearSelection('structureSection')">
                  Clear
                </button>
              </div>
            }
            <ul>
              @for (s of t.structureSections; track s.id) {
                <li class="click-row">
                  <label class="row-check" (click)="$event.stopPropagation()">
                    <input
                      type="checkbox"
                      [checked]="isRowSelected('structureSection', s.id)"
                      (change)="toggleRow('structureSection', s.id, $event)"
                    />
                  </label>
                  <span class="row-main" tabindex="0" (click)="openStructure(s)" (keydown.enter)="openStructure(s)">
                    <strong>{{ s.kind }}</strong>
                    {{ s.startOffset }}–{{ s.endOffset }}
                    @if (s.title) {
                      · {{ s.title }}
                    }
                    @if (s.notes) {
                      <span class="notes">{{ s.notes }}</span>
                    }
                    @if (s.isAiSuggested) {
                      <span class="badge">AI</span>
                    }
                  </span>
                </li>
              }
            </ul>
          </div>
          <div>
            <h3>Narrator passages</h3>
            @if (selection().narrative.size > 0) {
              <div class="bulk-bar">
                <span>{{ selection().narrative.size }} selected</span>
                <button
                  type="button"
                  [disabled]="timelineBulkBusy() || selection().narrative.size < 2"
                  (click)="mergeSelected('narrative')"
                >
                  Merge
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="deleteSelected('narrative')">
                  Delete
                </button>
                <button type="button" [disabled]="timelineBulkBusy()" (click)="clearSelection('narrative')">
                  Clear
                </button>
              </div>
            }
            <ul>
              @for (n of t.narratives; track n.id) {
                <li class="click-row">
                  <label class="row-check" (click)="$event.stopPropagation()">
                    <input
                      type="checkbox"
                      [checked]="isRowSelected('narrative', n.id)"
                      (change)="toggleRow('narrative', n.id, $event)"
                    />
                  </label>
                  <span class="row-main" tabindex="0" (click)="openNarrative(n)" (keydown.enter)="openNarrative(n)">
                    {{ n.startOffset }}–{{ n.endOffset }} · {{ n.perspectiveNotes || '—' }}
                    @if (n.isAiSuggested) {
                      <span class="badge">AI</span>
                    }
                  </span>
                </li>
              }
            </ul>
          </div>
        </section>
      }
    }

    @if (modal(); as m) {
      <div
        class="modal-backdrop"
        role="presentation"
        (click)="maybeCloseBackdrop($event)"
      ></div>
      <div class="modal-panel" role="dialog" aria-modal="true" [attr.aria-label]="modalAriaLabel(m)" (click)="$event.stopPropagation()">
        <div class="modal-head">
          <h3>{{ modalHeading(m) }}</h3>
          <button type="button" class="modal-close" [disabled]="modalBusy()" (click)="closeModal()">Close</button>
        </div>

        <div class="tabs">
          <button type="button" [class.active]="modalTab() === 'raw'" (click)="modalTab.set('raw')">Raw Markdown</button>
          <button type="button" [class.active]="modalTab() === 'formatted'" (click)="modalTab.set('formatted')">
            Formatted
          </button>
        </div>

        @if (modalTab() === 'raw') {
          <pre class="excerpt">{{ modalRawSlice() }}</pre>
        } @else {
          <div class="md-preview" [innerHTML]="formattedHtml()"></div>
        }

        @if (modalErr()) {
          <p class="modal-error">{{ modalErr() }}</p>
        }

        <section class="voice-panel">
          <h4>Voice</h4>

          @switch (m.kind) {
            @case ('utterance') {
              <p class="voice-meta">
                Saved kind: <strong>{{ m.utterance.speakerKind }}</strong>
                @if (m.utterance.speakerNeedsReview) {
                  <span class="badge warn">review</span>
                }
              </p>
              <label class="lbl">Speaker kind</label>
              <select
                class="full"
                [value]="uttSpeakerKindDraft"
                [disabled]="modalBusy()"
                (change)="onUtterSpeakerKindPick($event)"
              >
                <option value="character">Character (spoken dialogue)</option>
                <option value="quotedNonSpeech">Quoted emphasis (not speech)</option>
                <option value="narrator">Narrator</option>
              </select>
              <label class="lbl">Character profile</label>
              <select
                class="full"
                [value]="uttCharacterDraftId"
                [disabled]="modalBusy() || uttSpeakerKindDraft !== 'character'"
                (change)="onUtterCharacterPick($event)"
              >
                <option value="">(Unassigned)</option>
                @for (ch of characters(); track ch.id) {
                  <option [value]="ch.id">{{ ch.name }}</option>
                }
              </select>
              <div class="voice-grid">
                <div>
                  <label class="lbl">Gender presentation</label>
                  <input [(ngModel)]="voiceDraft.genderPresentation" name="vdGender" [ngModelOptions]="{ standalone: true }" />
                </div>
                <div>
                  <label class="lbl">Tone</label>
                  <input [(ngModel)]="voiceDraft.tone" name="vdTone" [ngModelOptions]="{ standalone: true }" />
                </div>
                <div>
                  <label class="lbl">Accent</label>
                  <input [(ngModel)]="voiceDraft.accent" name="vdAccent" [ngModelOptions]="{ standalone: true }" />
                </div>
                <div>
                  <label class="lbl">Breathiness</label>
                  <input [(ngModel)]="voiceDraft.breathiness" name="vdBreath" [ngModelOptions]="{ standalone: true }" />
                </div>
                <div>
                  <label class="lbl">Speaking pace</label>
                  <input [(ngModel)]="voiceDraft.speakingPace" name="vdPace" [ngModelOptions]="{ standalone: true }" />
                </div>
              </div>
              <div class="modal-actions">
                <button type="button" [disabled]="modalBusy()" (click)="saveUtteranceSpeaker(m.utterance)">
                  Save speaker
                </button>
                <button type="button" [disabled]="modalBusy()" (click)="saveUtteranceVoice()">Save voice traits</button>
              </div>
              <p class="fine-print">
                Voice traits apply to the selected character profile everywhere they speak. Save speaker commits kind and
                character link for this span only. Choose “Quoted emphasis” when quotation marks signal irony or emphasis,
                not spoken words.
              </p>
            }
            @case ('narrative') {
              @if (narrativeDraftVm) {
                <label class="lbl">Borrow narrator voice from character (optional)</label>
                <select
                  class="full"
                  [(ngModel)]="narrativeCharacterPickId"
                  name="narChar"
                  [ngModelOptions]="{ standalone: true }"
                  [disabled]="modalBusy()"
                >
                  <option value="">(None — use fields below)</option>
                  @for (ch of characters(); track ch.id) {
                    <option [value]="ch.id">{{ ch.name }}</option>
                  }
                </select>
                <label class="lbl">Perspective notes</label>
                <textarea
                  rows="3"
                  class="full"
                  [(ngModel)]="narrativeDraftVm.perspectiveNotes"
                  name="narPerspective"
                  [ngModelOptions]="{ standalone: true }"
                  [disabled]="modalBusy()"
                ></textarea>
                <div class="voice-grid">
                  <div>
                    <label class="lbl">Gender presentation</label>
                    <input
                      [(ngModel)]="narrativeDraftVm.genderPresentation"
                      name="narG"
                      [ngModelOptions]="{ standalone: true }"
                      [disabled]="modalBusy()"
                    />
                  </div>
                  <div>
                    <label class="lbl">Tone</label>
                    <input [(ngModel)]="narrativeDraftVm.tone" name="narT" [ngModelOptions]="{ standalone: true }" [disabled]="modalBusy()" />
                  </div>
                  <div>
                    <label class="lbl">Accent</label>
                    <input [(ngModel)]="narrativeDraftVm.accent" name="narA" [ngModelOptions]="{ standalone: true }" [disabled]="modalBusy()" />
                  </div>
                  <div>
                    <label class="lbl">Breathiness</label>
                    <input
                      [(ngModel)]="narrativeDraftVm.breathiness"
                      name="narB"
                      [ngModelOptions]="{ standalone: true }"
                      [disabled]="modalBusy()"
                    />
                  </div>
                  <div>
                    <label class="lbl">Speaking pace</label>
                    <input
                      [(ngModel)]="narrativeDraftVm.speakingPace"
                      name="narP"
                      [ngModelOptions]="{ standalone: true }"
                      [disabled]="modalBusy()"
                    />
                  </div>
                </div>
                <div class="modal-actions">
                  <button type="button" [disabled]="modalBusy()" (click)="saveNarrativePassage()">Save narrator passage</button>
                </div>
              }
            }
            @default {
              <p class="voice-note">
                This row only marks structure or quoted dialogue boundaries. Synthesis voices come from
                <strong>utterances</strong> (character dialogue) and <strong>narrator passages</strong> (exposition).
              </p>
            }
          }
        </section>
      </div>
    }
  `,
  styles: [
    `
      .hint {
        opacity: 0.85;
        max-width: 52rem;
      }
      .bulk-op-error {
        color: var(--sn-error);
        margin: 0 0 1rem;
        max-width: 52rem;
      }
      .stats {
        display: flex;
        gap: 1rem;
        flex-wrap: wrap;
        margin-bottom: 1rem;
        font-size: 0.9rem;
      }
      .prose pre {
        white-space: pre-wrap;
        font-family: Georgia, 'Times New Roman', serif;
        line-height: 1.45;
        max-height: 40vh;
        overflow: auto;
        border: 1px solid var(--sn-border);
        padding: 0.75rem;
        border-radius: 6px;
        background: var(--sn-soft-bg);
      }
      .lists {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
        gap: 1.5rem;
      }
      .lists ul {
        list-style: none;
        padding-left: 0;
        margin: 0;
      }
      .bulk-bar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 0.45rem;
        margin-bottom: 0.45rem;
        font-size: 0.84rem;
      }
      .bulk-bar button {
        cursor: pointer;
        padding: 0.28rem 0.55rem;
        border-radius: 6px;
        border: 1px solid var(--sn-border);
        background: var(--sn-soft-bg);
        font-size: 0.8rem;
      }
      .bulk-bar button:disabled {
        opacity: 0.55;
        cursor: not-allowed;
      }
      .click-row {
        display: flex;
        gap: 0.35rem;
        align-items: flex-start;
        border-radius: 4px;
        padding: 0.15rem 0;
      }
      .row-check {
        flex-shrink: 0;
        padding-top: 0.12rem;
        cursor: pointer;
      }
      .row-check input {
        cursor: pointer;
      }
      .row-main {
        flex: 1;
        min-width: 0;
        cursor: pointer;
      }
      .row-main:hover {
        opacity: 0.94;
      }
      .row-main:focus-visible {
        outline: 2px solid var(--sn-nav-active-bg);
        outline-offset: 2px;
      }
      .badge {
        margin-left: 0.35rem;
        font-size: 0.7rem;
        padding: 0.1rem 0.35rem;
        border-radius: 4px;
        background: var(--sn-chip-ai-bg);
        color: var(--sn-chip-ai-text);
      }
      .badge.warn {
        background: rgba(234, 179, 8, 0.25);
        color: var(--sn-text);
      }
      .notes {
        display: block;
        margin-top: 0.25rem;
        opacity: 0.85;
        font-size: 0.85rem;
      }
      .error {
        color: var(--sn-error);
      }

      .modal-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.45);
        z-index: 1000;
      }
      .modal-panel {
        position: fixed;
        z-index: 1001;
        left: 50%;
        top: 50%;
        transform: translate(-50%, -50%);
        width: min(52rem, calc(100vw - 2rem));
        max-height: min(88vh, 900px);
        overflow: auto;
        background: var(--sn-surface);
        border: 1px solid var(--sn-border);
        border-radius: 10px;
        padding: 1rem 1.25rem 1.25rem;
        box-shadow: 0 12px 40px rgba(0, 0, 0, 0.25);
      }
      .modal-head {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        gap: 1rem;
        margin-bottom: 0.75rem;
      }
      .modal-head h3 {
        margin: 0;
        font-size: 1.05rem;
      }
      button.modal-close {
        flex-shrink: 0;
        border: 1px solid var(--sn-border);
        background: transparent;
        border-radius: 6px;
        padding: 0.35rem 0.65rem;
        cursor: pointer;
      }
      .tabs {
        display: flex;
        gap: 0.35rem;
        margin-bottom: 0.65rem;
      }
      .tabs button {
        border: 1px solid var(--sn-border);
        background: transparent;
        padding: 0.35rem 0.75rem;
        border-radius: 6px;
        cursor: pointer;
        font-size: 0.88rem;
      }
      .tabs button.active {
        background: var(--sn-nav-active-bg);
        color: var(--sn-nav-active-text, #fff);
        border-color: transparent;
      }
      pre.excerpt {
        white-space: pre-wrap;
        word-break: break-word;
        font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
        font-size: 0.82rem;
        line-height: 1.45;
        max-height: 28vh;
        overflow: auto;
        margin: 0 0 1rem;
        padding: 0.65rem 0.75rem;
        border-radius: 6px;
        border: 1px solid var(--sn-border);
        background: var(--sn-soft-bg);
      }
      .md-preview {
        max-height: 28vh;
        overflow: auto;
        margin: 0 0 1rem;
        padding: 0.65rem 0.75rem;
        border-radius: 6px;
        border: 1px solid var(--sn-border);
        line-height: 1.5;
      }
      .md-preview :first-child {
        margin-top: 0;
      }
      .md-preview :last-child {
        margin-bottom: 0;
      }
      .md-preview code {
        font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
        font-size: 0.88em;
      }
      .voice-panel {
        border-top: 1px solid var(--sn-border);
        padding-top: 0.85rem;
      }
      .voice-panel h4 {
        margin: 0 0 0.5rem;
        font-size: 0.95rem;
      }
      .voice-meta {
        margin: 0 0 0.5rem;
        font-size: 0.88rem;
      }
      .lbl {
        display: block;
        font-size: 0.78rem;
        opacity: 0.85;
        margin: 0.35rem 0 0.15rem;
      }
      .full {
        width: 100%;
        box-sizing: border-box;
      }
      .voice-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
        gap: 0.35rem 0.65rem;
        margin-top: 0.35rem;
      }
      .modal-actions {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        margin-top: 0.65rem;
      }
      .modal-actions button {
        cursor: pointer;
        padding: 0.4rem 0.75rem;
        border-radius: 6px;
        border: 1px solid var(--sn-border);
        background: var(--sn-soft-bg);
      }
      .fine-print {
        font-size: 0.78rem;
        opacity: 0.82;
        margin: 0.5rem 0 0;
      }
      .voice-note {
        margin: 0;
        font-size: 0.88rem;
        opacity: 0.9;
      }
      .row-main span.sk.ai {
        opacity: 0.92;
      }
      .row-main span.sk.nonspeech {
        font-style: italic;
        opacity: 0.88;
      }
    `,
  ],
})
export class WorkTimelineComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(WorksApiService);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly data = signal<Timeline | null>(null);
  protected readonly characters = signal<CharacterVm[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly modal = signal<TimelineModal | null>(null);
  protected readonly modalTab = signal<'raw' | 'formatted'>('raw');
  protected readonly modalBusy = signal(false);
  protected readonly modalErr = signal<string | null>(null);
  protected readonly timelineBulkBusy = signal(false);
  protected readonly bulkOpError = signal<string | null>(null);
  protected readonly selection = signal<TimelineSelection>({
    utterance: new Set(),
    narrative: new Set(),
    dialogueSpan: new Set(),
    workChapter: new Set(),
    structureSection: new Set(),
  });

  /** Draft character id for utterance modal (HTML select uses string). */
  protected uttCharacterDraftId = '';

  /** Draft speaker kind for utterance modal (matches API JSON enum strings). */
  protected uttSpeakerKindDraft: 'character' | 'quotedNonSpeech' | 'narrator' = 'character';

  protected voiceDraft = {
    genderPresentation: 'unspecified',
    tone: 'neutral',
    accent: 'none',
    breathiness: 'normal',
    speakingPace: 'normal',
  };

  /** Populated while narrator-passage modal is open. */
  protected narrativeDraftVm: NarrativePassage | null = null;

  /** Parallel to draft: HTML select uses empty string for “no linked character”. */
  protected narrativeCharacterPickId = '';

  readonly formattedHtml = computed<SafeHtml>(() => {
    const raw = this.modalRawSlice();
    const html = marked(raw, { async: false }) as string;
    return this.sanitizer.bypassSecurityTrustHtml(html);
  });

  private readonly workId =
    this.route.parent?.snapshot.paramMap.get('id') ?? this.route.snapshot.paramMap.get('id')!;

  constructor() {
    this.reloadAll();
  }

  private cloneSelection(): TimelineSelection {
    const s = this.selection();
    return {
      utterance: new Set(s.utterance),
      narrative: new Set(s.narrative),
      dialogueSpan: new Set(s.dialogueSpan),
      workChapter: new Set(s.workChapter),
      structureSection: new Set(s.structureSection),
    };
  }

  protected isRowSelected(kind: TimelineBulkEntityKind, id: string): boolean {
    return this.selection()[kind].has(id);
  }

  protected toggleRow(kind: TimelineBulkEntityKind, id: string, ev: Event): void {
    ev.stopPropagation();
    const cb = ev.target as HTMLInputElement;
    const next = this.cloneSelection();
    if (cb.checked) next[kind].add(id);
    else next[kind].delete(id);
    this.selection.set(next);
  }

  protected clearSelection(kind: TimelineBulkEntityKind): void {
    const next = this.cloneSelection();
    next[kind].clear();
    this.selection.set(next);
  }

  private parseHttpErr(err: unknown): string {
    const e = err as { error?: unknown; message?: string };
    if (typeof e.error === 'string') return e.error;
    const o = e.error as { title?: string; detail?: string } | undefined;
    if (o && typeof o === 'object') return o.detail ?? o.title ?? e.message ?? 'Request failed.';
    return e.message ?? 'Request failed.';
  }

  protected mergeSelected(kind: TimelineBulkEntityKind): void {
    const ids = [...this.selection()[kind]];
    if (ids.length < 2) return;
    if (
      !confirm(
        `Merge ${ids.length} rows into one? Offsets become one union span; the earliest row keeps its id where applicable.`,
      )
    )
      return;

    this.timelineBulkBusy.set(true);
    this.bulkOpError.set(null);
    this.api.timelineBulkMerge(this.workId, { entityKind: kind, ids }).subscribe({
      next: (t) => {
        this.data.set(t);
        this.clearSelection(kind);
        this.timelineBulkBusy.set(false);
        this.closeModal();
      },
      error: (err) => {
        this.timelineBulkBusy.set(false);
        this.bulkOpError.set(this.parseHttpErr(err));
      },
    });
  }

  protected deleteSelected(kind: TimelineBulkEntityKind): void {
    const ids = [...this.selection()[kind]];
    if (ids.length === 0) return;
    if (!confirm(`Permanently delete ${ids.length} selected rows?`)) return;

    this.timelineBulkBusy.set(true);
    this.bulkOpError.set(null);
    this.api.timelineBulkDelete(this.workId, { entityKind: kind, ids }).subscribe({
      next: (t) => {
        this.data.set(t);
        this.clearSelection(kind);
        this.timelineBulkBusy.set(false);
        this.closeModal();
      },
      error: (err) => {
        this.timelineBulkBusy.set(false);
        this.bulkOpError.set(this.parseHttpErr(err));
      },
    });
  }

  @HostListener('document:keydown', ['$event'])
  protected onDocKey(ev: KeyboardEvent): void {
    if (ev.key === 'Escape' && this.modal()) {
      ev.preventDefault();
      this.closeModal();
    }
  }

  protected reloadAll(): void {
    this.error.set(null);
    this.bulkOpError.set(null);
    forkJoin({
      t: this.api.timeline(this.workId),
      c: this.api.characters(this.workId),
    }).subscribe({
      next: ({ t, c }) => {
        this.data.set(t);
        this.characters.set([...c]);
      },
      error: (err) =>
        this.error.set(typeof err.error === 'string' ? err.error : err.message ?? 'Failed to load summary'),
    });
  }

  protected modalHeading(m: TimelineModal): string {
    switch (m.kind) {
      case 'utterance':
        return `Utterance · ${m.utterance.startOffset}–${m.utterance.endOffset} · ${m.utterance.speakerKind}`;
      case 'dialogueSpan':
        return `Dialogue span · chapter slot ${m.span.orderIndexInChapter + 1} · ${m.span.startOffset}–${m.span.endOffset}`;
      case 'chapter':
        return `Chapter #${m.chapter.orderIndex + 1}${m.chapter.title ? ' · ' + m.chapter.title : ''} · ${m.chapter.startOffset}–${m.chapter.endOffset}`;
      case 'structure':
        return `Structure · ${m.section.kind} · ${m.section.startOffset}–${m.section.endOffset}`;
      case 'narrative':
        return `Narrator passage · ${m.narrative.startOffset}–${m.narrative.endOffset}`;
      default:
        return 'Inspect';
    }
  }

  protected modalAriaLabel(m: TimelineModal): string {
    return this.modalHeading(m);
  }

  protected modalOffsets(m: TimelineModal): { start: number; end: number } {
    switch (m.kind) {
      case 'utterance':
        return { start: m.utterance.startOffset, end: m.utterance.endOffset };
      case 'dialogueSpan':
        return { start: m.span.startOffset, end: m.span.endOffset };
      case 'chapter':
        return { start: m.chapter.startOffset, end: m.chapter.endOffset };
      case 'structure':
        return { start: m.section.startOffset, end: m.section.endOffset };
      case 'narrative':
        return { start: m.narrative.startOffset, end: m.narrative.endOffset };
    }
  }

  protected modalRawSlice(): string {
    const m = this.modal();
    const canon = this.data()?.canonicalText ?? '';
    if (!m || canon.length === 0) return '';
    const { start, end } = this.modalOffsets(m);
    const lo = Math.max(0, Math.min(start, canon.length));
    const hi = Math.max(lo, Math.min(end, canon.length));
    return canon.slice(lo, hi);
  }

  protected normalizeUtteranceKind(
    raw: string | undefined,
  ): 'character' | 'quotedNonSpeech' | 'narrator' {
    const k = (raw ?? '').replace(/\s+/g, '').toLowerCase();
    if (k === 'narrator') return 'narrator';
    if (k === 'quotednonspeech') return 'quotedNonSpeech';
    return 'character';
  }

  protected openUtterance(u: Utterance): void {
    this.modalErr.set(null);
    this.modalTab.set('raw');
    this.uttSpeakerKindDraft = this.normalizeUtteranceKind(u.speakerKind);
    this.uttCharacterDraftId = u.characterId ?? '';
    this.syncVoiceDraftFromCharacterId(this.uttCharacterDraftId || null);
    this.modal.set({ kind: 'utterance', utterance: u });
  }

  protected openDialogueSpan(span: DialogueSpan): void {
    this.modalErr.set(null);
    this.modalTab.set('raw');
    this.modal.set({ kind: 'dialogueSpan', span });
  }

  protected openChapter(chapter: WorkChapter): void {
    this.modalErr.set(null);
    this.modalTab.set('raw');
    this.modal.set({ kind: 'chapter', chapter });
  }

  protected openStructure(section: StoryStructureSection): void {
    this.modalErr.set(null);
    this.modalTab.set('raw');
    this.modal.set({ kind: 'structure', section });
  }

  protected openNarrative(n: NarrativePassage): void {
    this.modalErr.set(null);
    this.modalTab.set('raw');
    this.narrativeDraftVm = { ...n };
    this.narrativeCharacterPickId = n.narratorCharacterId ?? '';
    this.modal.set({ kind: 'narrative', narrative: n });
  }

  protected onUtterCharacterPick(ev: Event): void {
    const v = (ev.target as HTMLSelectElement).value;
    this.uttCharacterDraftId = v;
    this.syncVoiceDraftFromCharacterId(v.trim() ? v.trim() : null);
  }

  protected onUtterSpeakerKindPick(ev: Event): void {
    const v = (ev.target as HTMLSelectElement).value as typeof this.uttSpeakerKindDraft;
    this.uttSpeakerKindDraft = v;
    if (v !== 'character') {
      this.uttCharacterDraftId = '';
      this.syncVoiceDraftFromCharacterId(null);
    }
  }

  private syncVoiceDraftFromCharacterId(characterId: string | null): void {
    const c = characterId ? this.characters().find((ch) => ch.id === characterId) : undefined;
    if (c) {
      this.voiceDraft = {
        genderPresentation: c.genderPresentation,
        tone: c.tone,
        accent: c.accent,
        breathiness: c.breathiness,
        speakingPace: c.speakingPace,
      };
    } else {
      this.voiceDraft = {
        genderPresentation: 'unspecified',
        tone: 'neutral',
        accent: 'none',
        breathiness: 'normal',
        speakingPace: 'normal',
      };
    }
  }

  protected maybeCloseBackdrop(ev: MouseEvent): void {
    if (ev.target === ev.currentTarget) this.closeModal();
  }

  protected closeModal(): void {
    if (this.modalBusy()) return;
    this.modal.set(null);
    this.modalErr.set(null);
    this.narrativeDraftVm = null;
    this.narrativeCharacterPickId = '';
    this.uttSpeakerKindDraft = 'character';
    this.uttCharacterDraftId = '';
  }

  protected saveUtteranceSpeaker(u: Utterance): void {
    const kind = this.uttSpeakerKindDraft;
    const cid = kind === 'character' ? this.uttCharacterDraftId.trim() : '';
    const body: UtteranceUpsert = {
      id: u.id,
      speakerKind: kind,
      characterId: cid ? cid : null,
      userApproved: u.userApproved,
    };
    this.modalBusy.set(true);
    this.modalErr.set(null);
    this.api.updateUtterances(this.workId, [body]).subscribe({
      next: (list) => {
        const cur = this.data();
        if (cur) this.data.set({ ...cur, utterances: list });
        const updated = list.find((x) => x.id === u.id);
        if (updated) this.modal.set({ kind: 'utterance', utterance: updated });
        this.modalBusy.set(false);
        this.closeModal();
      },
      error: (err) => {
        this.modalBusy.set(false);
        this.modalErr.set(err.error ?? err.message ?? 'Could not save speaker.');
      },
    });
  }

  protected saveUtteranceVoice(): void {
    const cid = this.uttCharacterDraftId.trim();
    if (!cid) {
      this.modalErr.set('Choose a character before saving voice traits.');
      return;
    }

    const payload: CharacterUpsert[] = this.characters().map((c) => ({
      id: c.id,
      name: c.name,
      aliases: c.aliases,
      personalitySummary: c.personalitySummary ?? null,
      speechStyleSummary: c.speechStyleSummary ?? null,
      genderPresentation: c.id === cid ? this.voiceDraft.genderPresentation.trim() : c.genderPresentation,
      tone: c.id === cid ? this.voiceDraft.tone.trim() : c.tone,
      accent: c.id === cid ? this.voiceDraft.accent.trim() : c.accent,
      breathiness: c.id === cid ? this.voiceDraft.breathiness.trim() : c.breathiness,
      speakingPace: c.id === cid ? this.voiceDraft.speakingPace.trim() : c.speakingPace,
      userApproved: c.userApproved,
      patchAiExternalKey: false,
    }));

    this.modalBusy.set(true);
    this.modalErr.set(null);
    this.api.updateCharacters(this.workId, payload).subscribe({
      next: (list) => {
        this.characters.set(list.map((c) => ({ ...c })));
        this.syncVoiceDraftFromCharacterId(cid);
        this.modalBusy.set(false);
      },
      error: (err) => {
        this.modalBusy.set(false);
        this.modalErr.set(err.error ?? err.message ?? 'Could not save voice traits.');
      },
    });
  }

  protected saveNarrativePassage(): void {
    const n = this.narrativeDraftVm;
    if (!n) return;

    const narChar = this.narrativeCharacterPickId.trim();
    const body: NarrativePassageUpsert = {
      id: n.id,
      narratorCharacterId: narChar ? narChar : null,
      perspectiveNotes: n.perspectiveNotes ?? '',
      genderPresentation: n.genderPresentation.trim(),
      tone: n.tone.trim(),
      accent: n.accent.trim(),
      breathiness: n.breathiness.trim(),
      speakingPace: n.speakingPace.trim(),
    };

    this.modalBusy.set(true);
    this.modalErr.set(null);
    this.api.updateNarratives(this.workId, [body]).subscribe({
      next: (updatedList) => {
        const cur = this.data();
        if (cur) this.data.set({ ...cur, narratives: updatedList });
        const fresh = updatedList.find((x) => x.id === n.id);
        if (fresh) {
          this.narrativeDraftVm = { ...fresh };
          this.modal.set({ kind: 'narrative', narrative: fresh });
        }
        this.modalBusy.set(false);
        this.closeModal();
      },
      error: (err) => {
        this.modalBusy.set(false);
        this.modalErr.set(err.error ?? err.message ?? 'Could not save narrator passage.');
      },
    });
  }
}
