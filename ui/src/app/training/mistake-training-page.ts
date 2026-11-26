import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { Api, MistakeAnswer, MistakeItem, problemMessage } from '../api/api';
import { numberedLine } from '../engine/scores';
import { Board } from '../review/board';
import { INITIAL_FEN, openGameAt } from '../shared/chess';
import { TrainingNav } from './training-nav';

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
          <span class="hint" data-testid="mistakes-due">{{ summary() ? summary()!.due + ' due today' : '' }}</span>
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
export class MistakeTrainingPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly summary = signal<{ due: number; total: number } | null>(null);
  protected readonly item = signal<MistakeItem | null>(null);
  protected readonly answer = signal<MistakeAnswer | null>(null);
  protected readonly loading = signal(true);
  protected readonly checking = signal(false);
  protected readonly error = signal('');
  protected readonly fen = signal(INITIAL_FEN);
  protected readonly lastMove = signal<string | null>(null);
  private request: Subscription | undefined;

  protected readonly canAnswer = computed(() => !!this.item() && !this.answer() && !this.checking());
  /** "3...Nf6??": the Owner's move in the Game, with its classification's symbol. */
  protected readonly played = computed(() => {
    const it = this.item();
    return it ? numberedLine(it.ply, [it.playedSan + (it.classification === 'blunder' ? '??' : '?')]) : '';
  });
  protected readonly bestLine = computed(() => numberedLine(this.item()?.ply ?? 1, this.answer()?.bestLineSan ?? []));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    this.next();
  }

  protected next() {
    this.request?.unsubscribe(); // only the latest request shows its item
    this.answer.set(null);
    this.error.set('');
    this.api.mistakeTrainingSummary().subscribe((s) => this.summary.set(s));
    this.request = this.api.nextMistake().subscribe({
      next: (item) => {
        this.loading.set(false);
        this.item.set(item);
        if (item) this.show(item.fen, item.lastMove);
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
    this.checking.set(true);
    this.error.set('');
    this.api.answerMistake(item.itemId, uci).subscribe({
      next: (answer) => {
        this.checking.set(false);
        this.answer.set(answer);
        this.show(answer.fenAfter, answer.shown); // the Owner's move when right, the Engine's when wrong
      },
      error: (e) => {
        this.checking.set(false);
        this.error.set(problemMessage(e));
        this.show(item.fen, item.lastMove); // put the board back
      },
    });
  }

  /** Enter goes on; on a focused button the button's own click does, so it isn't done twice. */
  protected onEnter(event: Event) {
    if ((event.target as HTMLElement | null)?.closest('button')) return;
    if (this.answer()) this.next();
  }

  /** The Game at the position before the mistake. */
  protected openGame(item: MistakeItem) {
    openGameAt(this.router, item.gameId, item.ply - 1);
  }

  private show(fen: string, lastMove: string | null) {
    this.fen.set(fen);
    this.lastMove.set(lastMove);
  }
}
