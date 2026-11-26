import { Location } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, combineLatest, map, of, switchMap } from 'rxjs';
import { Api, MasterGame, problemMessage } from '../api/api';
import { Board } from '../review/board';
import { INITIAL_FEN } from '../shared/chess';

/**
 * A Master Game from Lichess, replayed read-only. It is not a Game: it is never stored in the
 * Collection, and nothing on this page can change it.
 */
@Component({
  selector: 'app-master-game',
  imports: [Board],
  host: { '(document:keydown)': 'onKey($event)' },
  template: `
    @if (game(); as g) {
      <div class="master-layout">
        <div class="board-column">
          <app-board [fen]="boardFen()" [lastMove]="lastMove()" [orientation]="flipped() ? 'black' : 'white'" [viewOnly]="true" />
          <div class="board-tools">
            <button type="button" (click)="ply.set(0)" title="Start (Home)" aria-label="Start">⏮</button>
            <button type="button" (click)="step(-1)" title="Back (←)" aria-label="Back">◀</button>
            <button type="button" (click)="step(1)" title="Forward (→)" aria-label="Forward">▶</button>
            <button type="button" (click)="ply.set(g.moves.length)" title="End (End)" aria-label="End">⏭</button>
            <button type="button" (click)="flipped.set(!flipped())">Flip board</button>
          </div>
        </div>
        <section class="card scoresheet-card" data-testid="master-game-view">
          <header class="sheet-head">
            <div class="sheet-player"><span class="colour-dot"></span><span class="name" data-testid="master-white">{{ g.white.name }}</span>
              <span class="elo">{{ g.white.elo ?? '' }}</span></div>
            <div class="sheet-player"><span class="colour-dot black"></span><span class="name">{{ g.black.name }}</span>
              <span class="elo">{{ g.black.elo ?? '' }}</span></div>
            <div class="sheet-meta"><span>{{ meta() }}</span><span class="sheet-result">{{ g.result }}</span></div>
            <p class="hint master-note">Master Game from Lichess, shown read-only. It is not part of your Collection.</p>
          </header>
          <div class="sheet master-moves">
            @for (m of g.moves; track $index; let i = $index) {
              @if (i % 2 === 0) { <span class="move-no">{{ i / 2 + 1 }}.</span> }
              <button type="button" class="move" data-testid="master-move" [class.current]="ply() === i + 1" (click)="ply.set(i + 1)">{{ m.san }}</button>
            }
          </div>
          <footer class="form-actions master-actions"><button type="button" (click)="back()">Back</button></footer>
        </section>
      </div>
    } @else if (error()) {
      <p class="empty" role="alert" data-testid="master-error">{{ error() }}</p>
    } @else {
      <p class="empty">Loading…</p>
    }
  `,
})
export class MasterGamePage {
  private readonly api = inject(Api);
  private readonly location = inject(Location);

  /** Route parameter: the Lichess game id. */
  readonly id = input.required<string>();
  /** Query parameter: open at this position if the game reaches it. */
  readonly fen = input<string | undefined>();

  protected readonly game = signal<MasterGame | null>(null);
  protected readonly error = signal('');
  protected readonly ply = signal(0);
  protected readonly flipped = signal(false);

  protected readonly current = computed(() => {
    const g = this.game();
    return g && this.ply() > 0 ? g.moves[this.ply() - 1] : null;
  });
  protected readonly boardFen = computed(() => this.current()?.fen ?? INITIAL_FEN);
  protected readonly lastMove = computed(() => this.current()?.uci ?? null);
  protected readonly meta = computed(() => {
    const g = this.game();
    return g ? [g.event, g.site, g.date].filter((x) => !!x).join(' · ') : '';
  });

  constructor() {
    // A new id or position replaces the game; only the latest request's answer is shown.
    combineLatest([toObservable(this.id), toObservable(this.fen)])
      .pipe(
        switchMap(([id, fen]) => {
          this.game.set(null);
          this.error.set('');
          return this.api.masterGame(id, fen).pipe(
            map((game) => ({ game, error: '' })),
            catchError((e) => of({ game: null, error: problemMessage(e) })),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe(({ game, error }) => {
        this.game.set(game);
        this.error.set(error);
        this.ply.set(game?.ply ?? 0);
      });
  }

  protected step(delta: number) {
    const total = this.game()?.moves.length ?? 0;
    this.ply.update((p) => Math.max(0, Math.min(total, p + delta)));
  }

  protected onKey(event: KeyboardEvent) {
    const total = this.game()?.moves.length ?? 0;
    const handled = {
      ArrowLeft: () => this.step(-1),
      ArrowRight: () => this.step(1),
      Home: () => this.ply.set(0),
      ArrowUp: () => this.ply.set(0),
      End: () => this.ply.set(total),
      ArrowDown: () => this.ply.set(total),
    }[event.key];
    if (!handled) return;
    handled();
    event.preventDefault();
  }

  protected back() {
    this.location.back();
  }
}
