import { CommonModule } from '@angular/common';

import {
  Component,
  HostListener,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';

import { FormsModule } from '@angular/forms';

import { ActivatedRoute } from '@angular/router';

import { forkJoin } from 'rxjs';

import {
  WorksApiService,
  CharacterVm,
  CharacterUpsert,
  Utterance,
} from '../../core/works-api.service';

@Component({
  selector: 'app-work-characters',

  standalone: true,

  imports: [CommonModule, FormsModule],

  template: `
    <h2>Characters &amp; voices</h2>

    <p class="hint">
      Profiles drive speech synthesis and <strong>re-analyze</strong> via the
      registry (names, aliases, AI key). Save changes <strong>per card</strong>.
      Use <strong>Merge selected into …</strong> on the keeper row to collapse
      duplicates. AI runs phased analysis that should only keep voices for
      people who <strong>actually speak</strong> (quoted dialogue) or act as an
      attributed <strong>narrator</strong>; remove bogus profiles with
      <strong>Remove profile…</strong>.       Lines flagged <span class="badge">review</span> need a speaker assignment.
      Spans tagged <span class="badge subtle">not speech</span> are quoted for
      emphasis (not spoken dialogue); correct those here too if analyze guessed wrong.

      <button type="button" class="linkish" (click)="reload()">
        Reload from server
      </button>
    </p>

    @if (error()) {
      <p class="error">{{ error() }}</p>
    } @else {
      <section class="cards">
        @for (c of rows(); track c.id) {
          <article class="card">
            <div class="card-head">
              <label class="lbl">Display name</label>

              <input [(ngModel)]="c.name" [name]="'n-' + c.id" />

              @if (c.isAiSuggested && !c.userApproved) {
                <span class="badge">AI</span>
              }
            </div>

            <label class="lbl">AI external key (stable slug for analyze)</label>

            <p class="microhint">
              Leave blank if none. Must be unique per work. Changing it affects
              how future analyze matches this profile.
            </p>

            <input
              class="mono-input"
              [ngModel]="c.aiExternalKey ?? ''"
              (ngModelChange)="onAiKeyChange(c, $event)"
              [name]="'aikey-' + c.id"
              placeholder="e.g. elena_vale"
              spellcheck="false"
              autocomplete="off"
            />

            <label class="lbl">Aliases — other names or forms of address</label>

            <p class="microhint">
              One per line (e.g. <code>Jim</code>, <code>honey</code>). Saved
              with this profile.
            </p>

            <textarea
              rows="3"
              [ngModel]="aliasDraft[c.id]"
              (ngModelChange)="setAliasDraft(c.id, $event)"
              [name]="'alias-' + c.id"
            ></textarea>

            <div class="grid4">
              <div>
                <label class="lbl">Gender</label>

                <input
                  [(ngModel)]="c.genderPresentation"
                  [name]="'g-' + c.id"
                />
              </div>

              <div>
                <label class="lbl">Tone</label>

                <input [(ngModel)]="c.tone" [name]="'t-' + c.id" />
              </div>

              <div>
                <label class="lbl">Accent</label>

                <input [(ngModel)]="c.accent" [name]="'a-' + c.id" />
              </div>

              <div>
                <label class="lbl">Pace / breath</label>

                <input
                  [(ngModel)]="c.speakingPace"
                  [name]="'p-' + c.id"
                  placeholder="pace"
                />

                <input
                  [(ngModel)]="c.breathiness"
                  [name]="'b-' + c.id"
                  placeholder="breath"
                />
              </div>
            </div>

            <label class="lbl">Personality &amp; motivations</label>

            <textarea
              [(ngModel)]="c.personalitySummary"
              rows="4"
              [name]="'ps-' + c.id"
            ></textarea>

            <label class="lbl"
              >Speech style (register, directness, relationships)</label
            >

            <textarea
              [(ngModel)]="c.speechStyleSummary"
              rows="4"
              [name]="'ss-' + c.id"
            ></textarea>

            <label class="chk">
              <input
                type="checkbox"
                [(ngModel)]="c.userApproved"
                [name]="'u-' + c.id"
              />
              User-approved profile
            </label>

            @if (otherRows(c.id).length > 0) {
              <div class="merge-into">
                <span class="lbl">Merge duplicates into this profile</span>

                <p class="microhint">
                  Tick other rows that are the same person, then merge —
                  utterances and narrator links move here.
                </p>

                <div class="merge-checks">
                  @for (o of otherRows(c.id); track o.id) {
                    <label class="merge-check">
                      <input
                        type="checkbox"
                        [checked]="mergePickSelected(c.id, o.id)"
                        (change)="
                          toggleMergeSource(
                            c.id,
                            o.id,
                            $any($event.target).checked
                          )
                        "
                      />

                      {{ o.name
                      }}{{ o.aiExternalKey ? ' · ' + o.aiExternalKey : '' }}
                    </label>
                  }
                </div>

                <button
                  type="button"
                  class="btn-merge"
                  [disabled]="mergeBusy() || !canMergeTarget(c.id)"
                  (click)="mergeIntoCard(c)"
                >
                  Merge selected into “{{ c.name }}”
                </button>
              </div>
            }

            <div class="card-actions">
              <button
                type="button"
                class="btn-save"
                [disabled]="saveBusyCardId() === c.id"
                (click)="saveCharacter(c)"
              >
                Save this profile
              </button>

              @if (savedCardId() === c.id) {
                <span class="ok">Saved</span>
              }

              <button
                type="button"
                class="btn-remove-profile"
                [disabled]="deleteBusy()"
                (click)="deleteProfile(c)"
              >
                Remove profile…
              </button>
            </div>
          </article>
        }
      </section>

      @if (speakerAttributionLines().length > 0) {
        <section class="review panel">
          <h3>Speaker attribution</h3>

          <p class="hint">
            Quoted spans below use canonical offsets. Set whether the quotes are
            <strong>spoken dialogue</strong> or <strong>emphasis only</strong>,
            then assign a character voice when it is spoken.
          </p>

          <div class="new-char">
            <label class="lbl">New character name</label>

            <input
              [(ngModel)]="newCharacterName"
              name="newCharName"
              placeholder="e.g. Concierge"
            />

            <button
              type="button"
              [disabled]="busySpeaker()"
              (click)="createCharacterAndReload()"
            >
              Create character
            </button>
          </div>

          @for (u of speakerAttributionLines(); track u.id) {
            <article class="review-row">
              <div class="meta">
                @if (
                  u.speakerNeedsReview &&
                  normalizeUtteranceKind(u.speakerKind) === 'character'
                ) {
                  <span class="badge">review</span>
                }
                @if (normalizeUtteranceKind(u.speakerKind) === 'quotedNonSpeech') {
                  <span class="badge subtle">not speech</span>
                }

                confidence {{ u.confidence | number: '1.2-2' }} · offsets
                {{ u.startOffset }}–{{ u.endOffset }}
              </div>

              <pre class="ctx">{{ excerptAround(u) }}</pre>

              <div class="assign">
                <label class="lbl">Quote role</label>

                <select
                  [ngModel]="kindDraft[u.id]"
                  (ngModelChange)="patchKindDraft(u.id, $event)"
                  [name]="'k-' + u.id"
                >
                  <option value="character">Spoken dialogue (character voice)</option>

                  <option value="quotedNonSpeech">
                    Quoted emphasis (not spoken)
                  </option>

                  <option value="narrator">Narrator</option>
                </select>

                <label class="lbl">Speaker profile</label>

                <select
                  [ngModel]="speakerPickDraft[u.id]"
                  (ngModelChange)="patchSpeakerPickDraft(u.id, $event)"
                  [disabled]="kindDraft[u.id] !== 'character'"
                  [name]="'sp-' + u.id"
                >
                  <option value="">(Unassigned)</option>

                  @for (c of rows(); track c.id) {
                    <option [value]="c.id">{{ c.name }}</option>
                  }
                </select>

                <button
                  type="button"
                  [disabled]="busySpeaker()"
                  (click)="applyUtteranceAttribution(u)"
                >
                  Apply
                </button>
              </div>
            </article>
          }
        </section>
      }
    }
  `,

  styles: [
    `
      .hint {
        opacity: 0.85;

        max-width: 52rem;
      }

      .cards {
        display: flex;

        flex-direction: column;

        gap: 1rem;

        margin: 1rem 0;
      }

      .card {
        border: 1px solid var(--sn-border);

        border-radius: 8px;

        padding: 0.75rem 1rem;

        background: var(--sn-surface);
      }

      .card-head {
        display: flex;

        align-items: center;

        gap: 0.5rem;

        margin-bottom: 0.5rem;
      }

      .card-head input[type='text'] {
        flex: 1;
      }

      .grid4 {
        display: grid;

        grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));

        gap: 0.5rem;

        margin-bottom: 0.5rem;
      }

      .lbl {
        display: block;

        font-size: 0.75rem;

        opacity: 0.85;

        margin-bottom: 0.15rem;
      }

      input[type='text'],
      textarea,
      select {
        width: 100%;

        box-sizing: border-box;
      }

      .mono-input {
        font-family:
          ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;

        font-size: 0.82rem;
      }

      textarea {
        font-family: inherit;

        font-size: 0.85rem;
      }

      .chk {
        display: flex;

        align-items: center;

        gap: 0.35rem;

        margin-top: 0.35rem;

        font-size: 0.85rem;
      }

      .badge {
        font-size: 0.7rem;

        padding: 0.1rem 0.35rem;

        border-radius: 4px;

        background: var(--sn-chip-ai-bg);

        color: var(--sn-chip-ai-text);
      }

      .badge.subtle {
        background: rgba(234, 179, 8, 0.22);

        color: var(--sn-text);
      }

      .ok {
        margin-left: 0.75rem;

        color: var(--sn-chip-user-text);
      }

      .error {
        color: var(--sn-error);
      }

      .linkish {
        margin-left: 0.5rem;

        padding: 0;

        border: none;

        background: none;

        color: var(--sn-nav-active-bg);

        text-decoration: underline;

        cursor: pointer;

        font: inherit;
      }

      .panel {
        margin-top: 2rem;

        padding-top: 1rem;

        border-top: 1px solid var(--sn-border);
      }

      .review-row {
        border: 1px dashed var(--sn-border);

        border-radius: 6px;

        padding: 0.65rem 0.85rem;

        margin-bottom: 0.75rem;

        background: var(--sn-surface);
      }

      .ctx {
        white-space: pre-wrap;

        word-break: break-word;

        font-size: 0.82rem;

        background: rgba(127, 127, 127, 0.08);

        padding: 0.5rem;

        border-radius: 4px;

        margin: 0.35rem 0;
      }

      .meta {
        font-size: 0.78rem;

        opacity: 0.9;
      }

      .assign {
        display: flex;

        flex-wrap: wrap;

        gap: 0.5rem;

        align-items: flex-end;
      }

      .assign select {
        max-width: 16rem;
      }

      .new-char {
        display: flex;

        flex-wrap: wrap;

        gap: 0.5rem;

        align-items: flex-end;

        margin-bottom: 1rem;
      }

      .microhint {
        font-size: 0.78rem;

        opacity: 0.82;

        margin: 0 0 0.25rem;
      }

      .merge-into {
        margin-top: 0.75rem;

        padding-top: 0.65rem;

        border-top: 1px dashed var(--sn-border);
      }

      .merge-checks {
        display: flex;

        flex-direction: column;

        gap: 0.35rem;

        margin: 0.35rem 0;
      }

      .merge-check {
        display: flex;

        align-items: center;

        gap: 0.45rem;

        font-size: 0.88rem;
      }

      button.btn-merge {
        margin-top: 0.35rem;

        padding: 0.25rem 0.6rem;

        font-size: 0.82rem;

        border-radius: 4px;

        border: 1px solid var(--sn-border);

        background: var(--sn-surface);

        cursor: pointer;
      }

      button.btn-merge:hover:not(:disabled) {
        border-color: var(--sn-nav-active-bg);
      }

      button.btn-merge:disabled {
        opacity: 0.45;

        cursor: not-allowed;
      }

      .card-actions {
        margin-top: 0.65rem;

        padding-top: 0.5rem;

        border-top: 1px dashed var(--sn-border);

        display: flex;

        flex-wrap: wrap;

        align-items: center;

        gap: 0.5rem;
      }

      button.btn-save {
        padding: 0.25rem 0.65rem;

        font-size: 0.85rem;

        border-radius: 4px;

        border: 1px solid var(--sn-nav-active-bg);

        background: var(--sn-nav-active-bg);

        color: var(--sn-bg, #fff);

        cursor: pointer;
      }

      button.btn-save:hover:not(:disabled) {
        filter: brightness(1.08);
      }

      button.btn-save:disabled {
        opacity: 0.45;

        cursor: not-allowed;
      }

      button.btn-remove-profile {
        padding: 0.2rem 0.55rem;

        font-size: 0.82rem;

        border-radius: 4px;

        border: 1px solid var(--sn-border);

        background: transparent;

        color: var(--sn-error, #f87171);

        cursor: pointer;
      }

      button.btn-remove-profile:hover:not(:disabled) {
        border-color: var(--sn-error, #f87171);
      }

      button.btn-remove-profile:disabled {
        opacity: 0.45;

        cursor: not-allowed;
      }
    `,
  ],
})
export class WorkCharactersComponent implements OnDestroy {
  private readonly route = inject(ActivatedRoute);

  private readonly api = inject(WorksApiService);

  protected readonly rows = signal<CharacterVm[]>([]);

  protected readonly utterances = signal<Utterance[]>([]);

  protected readonly canonicalText = signal('');

  protected readonly error = signal<string | null>(null);

  protected readonly busySpeaker = signal(false);

  protected readonly mergeBusy = signal(false);

  protected readonly deleteBusy = signal(false);

  protected readonly saveBusyCardId = signal<string | null>(null);

  protected readonly savedCardId = signal<string | null>(null);

  /** targetProfileId → sourceProfileId → true */

  protected readonly mergePick = signal<
    Record<string, Record<string, boolean>>
  >({});

  readonly speakerAttributionLines = computed(() =>
    this.utterances().filter((u) => {
      const kind = this.normalizeUtteranceKind(u.speakerKind);
      if (kind === 'quotedNonSpeech') return true;
      return kind === 'character' && u.speakerNeedsReview;
    }),
  );

  /** Draft speaker-kind per utterance id (matches API camelCase enum values). */
  protected kindDraft: Record<string, string> = {};

  protected speakerPickDraft: Record<string, string> = {};

  protected newCharacterName = '';

  protected aliasDraft: Record<string, string> = {};

  private readonly workId =
    this.route.parent?.snapshot.paramMap.get('id') ??
    this.route.snapshot.paramMap.get('id')!;

  private readonly excerptPad = 200;

  private savedFlashTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.reload();
  }

  ngOnDestroy(): void {
    if (this.savedFlashTimer) clearTimeout(this.savedFlashTimer);
  }

  @HostListener('document:visibilitychange')
  onDocVisibility(): void {
    if (document.visibilityState === 'visible') this.reload();
  }

  mergePickSelected(targetId: string, sourceId: string): boolean {
    return !!this.mergePick()[targetId]?.[sourceId];
  }

  otherRows(excludeId: string): CharacterVm[] {
    return this.rows().filter((r) => r.id !== excludeId);
  }

  protected toggleMergeSource(
    targetId: string,
    sourceId: string,
    checked: boolean,
  ): void {
    const prev = this.mergePick();

    const inner = { ...(prev[targetId] ?? {}) };

    if (checked) inner[sourceId] = true;
    else delete inner[sourceId];

    this.mergePick.set({ ...prev, [targetId]: inner });
  }

  mergeSources(targetId: string): string[] {
    const inner = this.mergePick()[targetId];

    if (!inner) return [];

    return Object.keys(inner).filter((id) => inner[id]);
  }

  canMergeTarget(targetId: string): boolean {
    return this.mergeSources(targetId).length >= 1;
  }

  protected onAiKeyChange(c: CharacterVm, raw: string): void {
    const t = raw.trim();

    c.aiExternalKey = t.length > 0 ? t : null;
  }

  excerptAround(u: Utterance): string {
    const text = this.canonicalText();

    if (!text) return '(Story text not loaded)';

    const pad = this.excerptPad;

    const lo = Math.max(0, u.startOffset - pad);

    const hi = Math.min(text.length, u.endOffset + pad);

    const before = text.slice(lo, u.startOffset);

    const mid = text.slice(u.startOffset, u.endOffset);

    const after = text.slice(u.endOffset, hi);

    return `${before}⟦${mid}⟧${after}`;
  }

  reload(onDone?: () => void): void {
    this.error.set(null);

    this.savedCardId.set(null);

    this.mergePick.set({});

    forkJoin({
      chars: this.api.characters(this.workId),

      utts: this.api.utterances(this.workId),

      detail: this.api.getWorkDetail(this.workId),
    }).subscribe({
      next: ({ chars, utts, detail }) => {
        this.rows.set(chars.map((c) => ({ ...c })));

        this.rebuildAliasDraft(chars);

        this.utterances.set([...utts]);

        this.rebuildUtteranceDrafts(utts);

        this.canonicalText.set(detail.canonicalText ?? '');

        onDone?.();
      },

      error: (err) => {
        this.error.set(err.message ?? 'Failed to load characters');

        onDone?.();
      },
    });
  }

  saveCharacter(c: CharacterVm): void {
    this.saveBusyCardId.set(c.id);

    this.error.set(null);

    this.savedCardId.set(null);

    if (this.savedFlashTimer) clearTimeout(this.savedFlashTimer);

    const payload: CharacterUpsert[] = [this.buildUpsert(c)];

    this.api.updateCharacters(this.workId, payload).subscribe({
      next: (list) => {
        this.rows.set(list.map((row) => ({ ...row })));

        this.rebuildAliasDraft(list);

        this.saveBusyCardId.set(null);

        this.savedCardId.set(c.id);

        this.savedFlashTimer = setTimeout(
          () => this.savedCardId.set(null),
          2800,
        );
      },

      error: (err: unknown) => {
        const msg =
          err && typeof err === 'object' && 'error' in err
            ? JSON.stringify((err as { error: unknown }).error)
            : err instanceof Error
              ? err.message
              : 'Save failed';

        this.error.set(msg);

        this.saveBusyCardId.set(null);
      },
    });
  }

  private buildUpsert(c: CharacterVm): CharacterUpsert {
    const key = c.aiExternalKey?.trim();

    return {
      id: c.id,

      name: c.name,

      aiExternalKey: key?.length ? key : null,

      patchAiExternalKey: true,

      aliases: this.parseAliasesLines(this.aliasDraft[c.id]),

      personalitySummary: c.personalitySummary ?? null,

      speechStyleSummary: c.speechStyleSummary ?? null,

      genderPresentation: c.genderPresentation,

      tone: c.tone,

      accent: c.accent,

      breathiness: c.breathiness,

      speakingPace: c.speakingPace,

      userApproved: c.userApproved,
    };
  }

  /** Public for template — normalize API speakerKind string. */
  protected normalizeUtteranceKind(
    raw: string | undefined,
  ): 'character' | 'quotedNonSpeech' | 'narrator' {
    const k = (raw ?? '').replace(/\s+/g, '').toLowerCase();
    if (k === 'narrator') return 'narrator';
    if (k === 'quotednonspeech') return 'quotedNonSpeech';
    return 'character';
  }

  protected patchKindDraft(id: string, value: string): void {
    this.kindDraft = { ...this.kindDraft, [id]: value };
  }

  protected patchSpeakerPickDraft(id: string, value: string): void {
    this.speakerPickDraft = { ...this.speakerPickDraft, [id]: value };
  }

  private rebuildUtteranceDrafts(utts: Utterance[]): void {
    const kd: Record<string, string> = {};
    const sd: Record<string, string> = {};
    for (const u of utts) {
      kd[u.id] = this.normalizeUtteranceKind(u.speakerKind);
      sd[u.id] = u.characterId ?? '';
    }
    this.kindDraft = kd;
    this.speakerPickDraft = sd;
  }

  applyUtteranceAttribution(u: Utterance): void {
    this.busySpeaker.set(true);

    const kind = this.kindDraft[u.id] ?? 'character';

    const characterIdRaw = (this.speakerPickDraft[u.id] ?? '').trim();

    const characterId =
      kind === 'character' && characterIdRaw.length > 0 ? characterIdRaw : null;

    this.api

      .updateUtterances(this.workId, [
        {
          id: u.id,

          speakerKind: kind,

          characterId,

          userApproved: u.userApproved,
        },
      ])

      .subscribe({
        next: (list) => {
          this.utterances.set(list);

          this.rebuildUtteranceDrafts(list);

          this.busySpeaker.set(false);
        },

        error: (err) => {
          this.error.set(err.message ?? 'Speaker update failed');

          this.busySpeaker.set(false);
        },
      });
  }

  protected setAliasDraft(characterId: string, value: string): void {
    this.aliasDraft = { ...this.aliasDraft, [characterId]: value };
  }

  deleteProfile(c: CharacterVm): void {
    const ok = confirm(
      `Remove profile "${c.name}"?\nUtterances that pointed at this character will lose that link (you can assign speakers again later).`,
    );

    if (!ok) return;

    this.deleteBusy.set(true);

    this.error.set(null);

    this.api.deleteCharacter(this.workId, c.id).subscribe({
      next: () => {
        this.reload(() => this.deleteBusy.set(false));
      },

      error: (err: unknown) => {
        this.error.set(
          err instanceof Error ? err.message : 'Remove profile failed',
        );

        this.deleteBusy.set(false);
      },
    });
  }

  mergeIntoCard(target: CharacterVm): void {
    const sources = this.mergeSources(target.id);

    if (sources.length === 0) {
      this.error.set(
        'Choose at least one other profile to merge into this one.',
      );

      return;
    }

    const ok = confirm(
      `Merge ${sources.length} profile(s) into "${target.name}"?\nUtterances and narrator links repoint here; absorbed names and keys become aliases.`,
    );

    if (!ok) return;

    this.mergeBusy.set(true);

    this.error.set(null);

    this.api
      .mergeCharacters(this.workId, {
        targetCharacterId: target.id,
        sourceCharacterIds: sources,
      })
      .subscribe({
        next: (list) => {
          this.rows.set(list.map((row) => ({ ...row })));

          this.rebuildAliasDraft(list);

          const prev = this.mergePick();

          const next = { ...prev };

          delete next[target.id];

          this.mergePick.set(next);

          this.mergeBusy.set(false);
        },

        error: (err: unknown) => {
          this.error.set(err instanceof Error ? err.message : 'Merge failed');

          this.mergeBusy.set(false);
        },
      });
  }

  private rebuildAliasDraft(chars: CharacterVm[]): void {
    const next: Record<string, string> = {};

    for (const c of chars) next[c.id] = (c.aliases ?? []).join('\n');

    this.aliasDraft = next;
  }

  private parseAliasesLines(raw: string | undefined): string[] | null {
    const lines = (raw ?? '')

      .split(/\r?\n/)

      .map((s) => s.trim())

      .filter((s) => s.length > 0);

    const out: string[] = [];

    const seen = new Set<string>();

    for (const line of lines) {
      const key = line.toLowerCase();

      if (seen.has(key)) continue;

      seen.add(key);

      out.push(line);
    }

    return out.length ? out : null;
  }

  createCharacterAndReload(): void {
    const name = this.newCharacterName.trim();

    if (!name) {
      this.error.set('Enter a name for the new character.');

      return;
    }

    this.busySpeaker.set(true);

    this.api.createCharacter(this.workId, { name }).subscribe({
      next: () => {
        this.newCharacterName = '';

        this.reload();

        this.busySpeaker.set(false);
      },

      error: (err) => {
        this.error.set(err.message ?? 'Create character failed');

        this.busySpeaker.set(false);
      },
    });
  }
}
