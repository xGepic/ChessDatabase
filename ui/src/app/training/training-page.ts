import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { RouterLink } from '@angular/router';
import { Api, RepertoireTrainingItem, TrainingAnswer, problemMessage } from '../api/api';
import { numberedChoices, numberedLine } from '../engine/scores';
import { Board } from '../review/board';
import { INITIAL_FEN } from '../shared/chess';

/** How long the Opponent's move takes to appear, so the Owner sees it played. */
const OPPONENT_DELAY_MS = 450;

/**
 * Repertoire Training: the app plays the line up to the Opponent's move, the Owner answers on the
 * board with their Repertoire move. Wrong answers show the right move and come back soon; right
 * answers come back later (spaced repetition).
 */
@Component({
  selector: 'app-training-page',
  imports: [Board, RouterLink],
  host: { '(document:keydown.enter)': 'onEnter($event)' },
  template: `
    <div class="training">
      <div class="board-column">
        <app-board [fen]="fen()" [orientation]="item()?.side ?? 'white'" [lastMove]="lastMove()" [viewOnly]="!canAnswer()"
                   (move)="onMove($event)" />
      </div>
      <section class="card training-card" aria-label="Repertoire Training">
        <header class="card-head">
          <h1>Repertoire Training</h1>
          <span class="spacer"></span>
          <span class="hint" data-testid="training-due">{{ dueLabel() }}</span>
        </header>
        <div class="card-body">
          @if (error()) { <p class="board-error" role="alert">{{ error() }}</p> }
          @if (loading()) {
            <p class="hint">Loading…</p>
          } @else if (item(); as it) {
            <p class="training-line mono" data-testid="training-line">{{ lineText() || 'The starting position' }}</p>
            @if (answer(); as a) {
              @if (a.correct) {
                <p class="training-verdict right" role="status" data-testid="training-verdict">Right. {{ alsoText() }}</p>
              } @else {
                <p class="training-verdict wrong" role="status" data-testid="training-verdict">Not your Repertoire move. Your Repertoire plays
                  <strong class="mono">{{ expectedText() }}</strong>. It comes back soon.</p>
              }
              <button type="button" class="primary" data-testid="training-next" (click)="next()">Next (Enter)</button>
            } @else if (canAnswer()) {
              <p data-testid="training-prompt">{{ it.new ? 'New: ' : '' }}What do you play with {{ it.side === 'white' ? 'White' : 'Black' }}? Play your Repertoire move on the board.</p>
            } @else {
              <p class="hint">Your Opponent is moving…</p>
            }
          } @else if (summary(); as s) {
            @if (s.total === 0) {
              <p class="empty" data-testid="training-empty">Your Repertoire has no positions to train yet. <a routerLink="/repertoire">Build your Repertoire</a>.</p>
            } @else {
              <p class="empty" data-testid="training-done">Nothing more is due today. {{ s.total }} positions in training.</p>
            }
          }
        </div>
      </section>
    </div>
  `,
})
export class TrainingPage {
  private readonly api = inject(Api);
  private readonly board = viewChild(Board);

  protected readonly summary = signal<{ due: number; total: number } | null>(null);
  protected readonly item = signal<RepertoireTrainingItem | null>(null);
  protected readonly answer = signal<TrainingAnswer | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  /** What the board shows: the position before the Opponent's move first, then after it, then after the answer. */
  protected readonly fen = signal(INITIAL_FEN);
  protected readonly lastMove = signal<string | null>(null);
  private readonly ready = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private request: Subscription | undefined;

  protected readonly canAnswer = computed(() => this.ready() && !!this.item() && !this.answer());
  protected readonly dueLabel = computed(() => {
    const s = this.summary();
    return s ? `${s.due} due today` : '';
  });
  protected readonly lineText = computed(() => numberedLine(1, (this.item()?.line ?? []).map((m) => m.san)));
  private readonly ply = computed(() => (this.item()?.line.length ?? 0) + 1);
  protected readonly expectedText = computed(() => numberedChoices(this.ply(), this.answer()?.expected ?? []));
  /** After a right answer, the other moves the Repertoire also has here. */
  protected readonly alsoText = computed(() => {
    const a = this.answer();
    if (!a || a.expected.length < 2) return '';
    return `Your Repertoire also has ${a.expected.length - 1 === 1 ? 'another move' : 'other moves'} here: ${this.expectedText()}.`;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.timer);
      this.request?.unsubscribe();
    });
    this.next();
  }

  /** Loads the next due item and plays the Opponent's move onto the board. */
  protected next() {
    clearTimeout(this.timer);
    this.request?.unsubscribe(); // only the latest request shows its item
    this.answer.set(null);
    this.ready.set(false);
    this.error.set('');
    this.api.repertoireTrainingSummary().subscribe((s) => this.summary.set(s));
    this.request = this.api.nextRepertoireItem().subscribe({
      next: (item) => {
        this.loading.set(false);
        this.item.set(item);
        if (!item) return;
        const line = item.line;
        if (!item.fenBefore) {
          this.show(item.fen, null);
          this.ready.set(true);
          return;
        }
        this.show(item.fenBefore, line.length > 1 ? line[line.length - 2].uci : null);
        clearTimeout(this.timer);
        this.timer = setTimeout(() => {
          this.show(item.fen, line[line.length - 1].uci);
          this.ready.set(true);
        }, OPPONENT_DELAY_MS);
      },
      error: (e) => {
        this.loading.set(false);
        this.error.set(problemMessage(e));
      },
    });
  }

  protected onMove(uci: string) {
    const item = this.item();
    if (!item || !this.canAnswer()) return;
    this.api.answerRepertoireItem(item.itemId, uci).subscribe({
      next: (answer) => {
        this.answer.set(answer);
        this.show(answer.fenAfter, answer.shown); // the Owner's move when right, the Repertoire's when wrong
      },
      error: (e) => {
        this.error.set(problemMessage(e));
        this.board()?.reset();
      },
    });
  }

  /** Enter goes on; on the Next button itself the button's own click does, so it isn't done twice. */
  protected onEnter(event: Event) {
    if ((event.target as HTMLElement | null)?.closest('button')) return;
    if (this.answer()) this.next();
  }

  private show(fen: string, lastMove: string | null) {
    this.fen.set(fen);
    this.lastMove.set(lastMove);
  }
}
