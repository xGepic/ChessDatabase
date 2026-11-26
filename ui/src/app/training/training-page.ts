import { Component, computed, inject, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, RepertoireTrainingItem, TrainingAnswer } from '../api/api';
import { numberedChoices, numberedLine } from '../engine/scores';
import { Board } from '../review/board';
import { TrainingNav } from './training-nav';
import { TrainingSession } from './training-session';

/** How long the Opponent's move takes to appear, so the Owner sees it played. */
const OPPONENT_DELAY_MS = 450;

/**
 * Repertoire Training: the app plays the line up to the Opponent's move, the Owner answers on the
 * board with their Repertoire move. Wrong answers show the right move and come back soon; right
 * answers come back later (spaced repetition).
 */
@Component({
  selector: 'app-training-page',
  imports: [Board, RouterLink, TrainingNav],
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
        <app-training-nav />
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
            } @else if (canAnswer() || checking()) {
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
export class TrainingPage extends TrainingSession<RepertoireTrainingItem, TrainingAnswer> {
  private readonly api = inject(Api);
  private readonly board = viewChild(Board);
  private timer: ReturnType<typeof setTimeout> | undefined;

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
    super();
    this.next();
  }

  protected summaryRequest() {
    return this.api.repertoireTrainingSummary();
  }

  protected nextRequest() {
    return this.api.nextRepertoireItem();
  }

  protected answerRequest(itemId: number, uci: string) {
    return this.api.answerRepertoireItem(itemId, uci);
  }

  /** Shows the position before the Opponent's move, then plays it. */
  protected present(item: RepertoireTrainingItem) {
    const line = item.line;
    if (!item.fenBefore) {
      this.show(item.fen, null);
      this.ready.set(true);
      return;
    }
    this.show(item.fenBefore, line.length > 1 ? line[line.length - 2].uci : null);
    this.timer = setTimeout(() => {
      this.show(item.fen, line[line.length - 1].uci);
      this.ready.set(true);
    }, OPPONENT_DELAY_MS);
  }

  protected restore() {
    this.board()?.reset();
  }

  protected override stop() {
    clearTimeout(this.timer);
  }
}
