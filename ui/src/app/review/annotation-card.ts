import { Component, OnDestroy, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Api, Game, MoveNode, problemMessage } from '../api/api';
import { DIAGRAM_NAG, MOVE_SYMBOLS, POSITION_SYMBOLS, toggleNag } from '../shared/nags';
import { moveLabel } from './notation';
import { Icon } from '../shared/icon';

const COMMENT_DEBOUNCE_MS = 500;

/**
 * Annotates the current move: symbols from a quick picker and a text comment. Comments save
 * shortly after typing stops, and any pending text is saved before moving to another move.
 */
@Component({
  selector: 'app-annotation-card',
  imports: [Icon],
  template: `
    <section class="annotation" aria-label="Annotation" data-testid="annotation-card">
      @if (node(); as n) {
        <div class="annotation-body">
          <div class="annotation-head">
            <span class="annotation-title">Your note on <span class="mono current-move" data-testid="annotated-move">{{ annotated() }}</span></span>
            <span class="hint annotation-status">
              @if (error()) {
                <span class="field-error">{{ error() }}</span>
              } @else {
                <span data-testid="comment-status">{{ status() }}</span>
              }
            </span>
            <button type="button" class="quiet symbols-toggle" data-testid="symbols-toggle" [attr.aria-expanded]="symbolsOpen()"
                    aria-controls="annotation-symbols" (click)="symbolsOpen.set(!symbolsOpen())">
              Symbols<app-icon name="chevron" [class.open]="symbolsOpen()" />
            </button>
          </div>
          <div class="symbols" id="annotation-symbols" [hidden]="!symbolsOpen()">
            <div class="symbol-row">
              <div class="symbol-group" role="group" aria-label="Move symbols">
                @for (s of moveSymbols; track s.nag) {
                  <button type="button" class="symbol" [class.on]="n.nags.includes(s.nag)" [attr.aria-pressed]="n.nags.includes(s.nag)"
                          [title]="s.name" [attr.data-testid]="'nag-' + s.nag" (click)="toggle(s.nag)">{{ s.glyph }}</button>
                }
              </div>
              <div class="symbol-group" role="group" aria-label="Diagram">
                <button type="button" class="symbol diagram-mark" [class.on]="n.nags.includes(diagramNag)" [attr.aria-pressed]="n.nags.includes(diagramNag)"
                        title="Print a board diagram after this move in PDF export" data-testid="diagram-mark" (click)="toggle(diagramNag)">Diagram</button>
              </div>
            </div>
            <div class="symbol-group" role="group" aria-label="Position symbols">
              @for (s of positionSymbols; track s.nag) {
                <button type="button" class="symbol" [class.on]="n.nags.includes(s.nag)" [attr.aria-pressed]="n.nags.includes(s.nag)"
                        [title]="s.name" [attr.data-testid]="'nag-' + s.nag" (click)="toggle(s.nag)">{{ s.glyph }}</button>
              }
            </div>
          </div>
          <label class="sr-only" for="comment">Comment after {{ label() }}</label>
          <textarea id="comment" data-testid="comment-input" rows="2" placeholder="Comment on this move…"
                    [value]="draft()" (input)="onInput($any($event.target).value)" (blur)="flush()"></textarea>
        </div>
      } @else {
        <p class="annotation-empty">Select a move to annotate it.</p>
      }
    </section>
  `,
})
export class AnnotationCard implements OnDestroy {
  private readonly api = inject(Api);

  readonly game = input.required<Game>();
  readonly node = input<MoveNode | null>(null);
  readonly saved = output<Game>();

  protected readonly moveSymbols = MOVE_SYMBOLS;
  protected readonly positionSymbols = POSITION_SYMBOLS;
  protected readonly diagramNag = DIAGRAM_NAG;
  protected readonly draft = signal('');
  protected readonly status = signal('');
  protected readonly error = signal('');
  /** The symbol picker is opened on demand; the heading shows the move's symbols either way. */
  protected readonly symbolsOpen = signal(false);
  /** The move with its symbols, e.g. "15.f5? ±" (plus a note when it is marked for a diagram). */
  protected readonly annotated = computed(() => {
    const n = this.node();
    if (!n) return '';
    const text = moveLabel({ ...n, nags: n.nags.filter((nag) => nag !== DIAGRAM_NAG) }, true);
    return n.nags.includes(DIAGRAM_NAG) ? text + ' · diagram' : text;
  });
  protected readonly label = computed(() => {
    const n = this.node();
    return n ? moveLabel({ ...n, nags: [] }, true) : '';
  });

  private readonly nodeId = computed(() => this.node()?.id ?? null);
  private pending: { gameId: number; nodeId: number; text: string } | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    // A different move: save what was typed for the previous one, then show this one's comment.
    effect(() => {
      this.nodeId();
      untracked(() => {
        this.flush();
        this.draft.set(this.node()?.commentAfter ?? '');
        this.status.set('');
        this.error.set('');
      });
    });
  }

  ngOnDestroy() {
    this.flush();
  }

  protected onInput(text: string) {
    const node = this.node();
    if (!node) return;
    this.draft.set(text);
    this.status.set('Typing…');
    this.pending = { gameId: this.game().id, nodeId: node.id, text };
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush(), COMMENT_DEBOUNCE_MS);
  }

  protected flush() {
    clearTimeout(this.timer);
    const pending = this.pending;
    if (!pending) return;
    this.pending = null;
    this.api.setComment(pending.gameId, pending.nodeId, pending.text).subscribe({
      next: (game) => {
        if (this.nodeId() === pending.nodeId && !this.pending) this.status.set('Saved');
        this.saved.emit(game);
      },
      error: (e) => this.error.set(problemMessage(e)),
    });
  }

  protected toggle(nag: number) {
    const node = this.node();
    if (!node) return;
    this.error.set('');
    this.api.setNags(this.game().id, node.id, toggleNag(node.nags, nag)).subscribe({
      next: (game) => this.saved.emit(game),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }
}
