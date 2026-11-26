import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Chess } from 'chess.js';
import { Api, RepertoireTrainingItem, TrainingAnswer, problemMessage } from '../api/api';
import { numberedLine } from '../engine/scores';
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
  host: { '(document:keydown.enter)': 'onEnter()' },
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

  protected readonly canAnswer = computed(() => this.ready() && !!this.item() && !this.answer());
  protected readonly dueLabel = computed(() => {
    const s = this.summary();
    return s ? `${s.due} due today` : '';
  });
  protected readonly lineText = computed(() => numberedLine(1, (this.item()?.line ?? []).map((m) => m.san)));
  private readonly ply = computed(() => (this.item()?.line.length ?? 0) + 1);
  protected readonly expectedText = computed(() =>
    (this.answer()?.expected ?? []).map((san) => numberedLine(this.ply(), [san])).join(' or '),
  );
  /** After a right answer, the other moves the Repertoire also has here. */
  protected readonly alsoText = computed(() => {
    const a = this.answer();
    if (!a || a.expected.length < 2) return '';
    return `Your Repertoire also has ${a.expected.length - 1 === 1 ? 'another move' : 'other moves'} here: ${this.expectedText()}.`;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    this.next();
  }

  /** Loads the next due item and plays the Opponent's move onto the board. */
  protected next() {
    clearTimeout(this.timer);
    this.answer.set(null);
    this.ready.set(false);
    this.error.set('');
    this.api.repertoireTrainingSummary().subscribe((s) => this.summary.set(s));
    this.api.nextRepertoireItem().subscribe({
      next: (item) => {
        this.loading.set(false);
        this.item.set(item);
        if (!item) return;
        const line = item.line;
        if (line.length === 0) {
          this.show(item.fen, null);
          this.ready.set(true);
          return;
        }
        const before = new Chess();
        for (const m of line.slice(0, -1)) before.move(uciMove(m.uci));
        this.show(before.fen(), line.length > 1 ? line[line.length - 2].uci : null);
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
        // Show the move that counts: the Owner's when right, the Repertoire's when wrong.
        const shown = answer.correct ? uci : answer.expectedUci[0];
        const after = new Chess(item.fen);
        after.move(uciMove(shown));
        this.show(after.fen(), shown);
      },
      error: (e) => {
        this.error.set(problemMessage(e));
        this.board()?.reset();
      },
    });
  }

  protected onEnter() {
    if (this.answer()) this.next();
  }

  private show(fen: string, lastMove: string | null) {
    this.fen.set(fen);
    this.lastMove.set(lastMove);
  }
}

function uciMove(uci: string) {
  return { from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] };
}
