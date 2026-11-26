import { Component, OnDestroy, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Api, Game, MoveNode, problemMessage } from '../api/api';
import { MOVE_SYMBOLS, POSITION_SYMBOLS, toggleNag } from '../shared/nags';
import { moveLabel } from './notation';

const COMMENT_DEBOUNCE_MS = 500;

/**
 * Annotates the current move: symbols from a quick picker and a text comment. Comments save
 * shortly after typing stops, and any pending text is saved before moving to another move.
 */
@Component({
  selector: 'app-annotation-card',
  template: `
    <section class="card side-card" data-testid="annotation-card">
      <header class="card-head">
        <h2>Annotation</h2>
        <span class="spacer"></span>
        @if (node(); as n) { <span class="mono current-move">{{ label() }}</span> }
      </header>
      @if (node(); as n) {
        <div class="annotation-body">
          <div class="symbol-group" role="group" aria-label="Move symbols">
            @for (s of moveSymbols; track s.nag) {
              <button type="button" class="symbol" [class.on]="n.nags.includes(s.nag)" [attr.aria-pressed]="n.nags.includes(s.nag)"
                      [title]="s.name" [attr.data-testid]="'nag-' + s.nag" (click)="toggle(s.nag)">{{ s.glyph }}</button>
            }
          </div>
          <div class="symbol-group" role="group" aria-label="Position symbols">
            @for (s of positionSymbols; track s.nag) {
              <button type="button" class="symbol" [class.on]="n.nags.includes(s.nag)" [attr.aria-pressed]="n.nags.includes(s.nag)"
                      [title]="s.name" [attr.data-testid]="'nag-' + s.nag" (click)="toggle(s.nag)">{{ s.glyph }}</button>
            }
          </div>
          <label class="sr-only" for="comment">Comment after {{ label() }}</label>
          <textarea id="comment" data-testid="comment-input" rows="4" placeholder="Comment on this move…"
                    [value]="draft()" (input)="onInput($any($event.target).value)" (blur)="flush()"></textarea>
          <p class="hint">
            @if (error()) {
              <span class="field-error">{{ error() }}</span>
            } @else {
              <span data-testid="comment-status">{{ status() }}</span>
            }
          </p>
        </div>
      } @else {
        <p class="empty">Select a move to annotate it.</p>
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
  protected readonly draft = signal('');
  protected readonly status = signal('');
  protected readonly error = signal('');
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
