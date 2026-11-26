import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, MistakeAnswer, MistakeItem } from '../api/api';
import { numberedLine } from '../engine/scores';
import { Board } from '../review/board';
import { openGameAt } from '../shared/chess';
import { TrainingNav } from './training-nav';
import { TrainingSession } from './training-session';

/**
 * Mistake Training: positions from the Owner's analysed Games where the Owner made a mistake or
 * blunder. Find a better move; the Engine's best line is shown afterwards, and the source Game
 * opens at the position. Spaced repetition brings missed positions back sooner.
 */
@Component({
  selector: 'app-mistake-training-page',
  imports: [Board, RouterLink, TrainingNav],
  host: { '(document:keydown.enter)': 'onEnter($event)' },
  template: `
    <div class="training">
      <div class="board-column">
        <app-board [fen]="fen()" [orientation]="item()?.side ?? 'white'" [lastMove]="lastMove()" [viewOnly]="!canAnswer()"
                   (move)="onMove($event)" />
      </div>
      <section class="card training-card" aria-label="Mistake Training">
        <header class="card-head">
          <h1>Mistake Training</h1>
          <span class="spacer"></span>
          <span class="hint" data-testid="mistakes-due">{{ dueLabel() }}</span>
        </header>
        <app-training-nav />
        <div class="card-body">
          @if (error()) { <p class="board-error" role="alert">{{ error() }}</p> }
          @if (loading()) {
            <p class="hint">Loading…</p>
          } @else if (item(); as it) {
            <p class="hint" data-testid="mistake-source">Your Game against {{ it.opponent }}{{ it.date ? ', ' + it.date : '' }}</p>
            @if (answer(); as a) {
              @if (a.correct) {
                <p class="training-verdict right" role="status" data-testid="mistake-verdict">Right: that keeps your position.</p>
              } @else {
                <p class="training-verdict wrong" role="status" data-testid="mistake-verdict">Not good enough. It comes back soon.</p>
              }
              <p data-testid="mistake-best-line">The Engine's best line: <strong class="mono">{{ bestLine() }}</strong></p>
              <div class="training-actions">
                <button type="button" class="primary" data-testid="mistake-next" (click)="next()">Next (Enter)</button>
                <button type="button" data-testid="mistake-open-game" (click)="openGame(it)">Open source Game</button>
              </div>
            } @else {
              <p data-testid="mistake-prompt">{{ it.new ? 'New: ' : '' }}You played <strong class="mono">{{ played() }}</strong> here.
                Find a better move for {{ it.side === 'white' ? 'White' : 'Black' }}.</p>
              @if (checking()) { <p class="hint">The Engine is judging your move…</p> }
              <button type="button" class="link-button" data-testid="mistake-open-game" (click)="openGame(it)">Open source Game</button>
            }
          } @else if (summary(); as s) {
            @if (s.total === 0) {
              <p class="empty" data-testid="mistakes-empty">No mistakes to train yet. Run Game Analysis on your Games
                (<a routerLink="/games">Games</a>); your mistakes and blunders become positions here.</p>
            } @else {
              <p class="empty" data-testid="mistakes-done">Nothing more is due today. {{ s.total }} positions in training.</p>
            }
          }
        </div>
      </section>
    </div>
  `,
})
export class MistakeTrainingPage extends TrainingSession<MistakeItem, MistakeAnswer> {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** "3...Nf6??": the Owner's move in the Game, with its classification's symbol. */
  protected readonly played = computed(() => {
    const it = this.item();
    return it ? numberedLine(it.ply, [it.playedSan + (it.classification === 'blunder' ? '??' : '?')]) : '';
  });
  protected readonly bestLine = computed(() => numberedLine(this.item()?.ply ?? 1, this.answer()?.bestLineSan ?? []));

  constructor() {
    super();
    this.next();
  }

  protected summaryRequest() {
    return this.api.mistakeTrainingSummary();
  }

  protected nextRequest() {
    return this.api.nextMistake();
  }

  /** Moves other than the best one are judged by the Engine, which takes a moment. */
  protected answerRequest(itemId: number, uci: string) {
    return this.api.answerMistake(itemId, uci);
  }

  protected present(item: MistakeItem) {
    this.show(item.fen, item.lastMove);
    this.ready.set(true);
  }

  protected restore(item: MistakeItem) {
    this.present(item);
  }

  /** The Game at the position before the mistake. */
  protected openGame(item: MistakeItem) {
    openGameAt(this.router, item.gameId, item.ply - 1);
  }
}
