import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { Api, BoardMove, Game, MasterGameRef, Masters, OpeningTree, PositionGame, problemMessage, resultLabel } from '../api/api';

/**
 * The Opening Tree for the board position, from the Owner's Games: every move played from here
 * (main lines only), how often and how the Owner scored, and the Games that reached the position.
 */
@Component({
  selector: 'app-opening-tree',
  template: `
    <section class="card tree-card" aria-label="Opening Tree" data-testid="opening-tree">
      <header class="card-head">
        <h2>Opening Tree</h2>
        <div class="segmented tree-sources" role="tablist" aria-label="Source">
          <button type="button" role="tab" data-testid="tree-source-mine" [attr.aria-selected]="source() === 'mine'"
                  [class.on]="source() === 'mine'" (click)="source.set('mine')">My Games</button>
          <button type="button" role="tab" data-testid="tree-source-masters" [attr.aria-selected]="source() === 'masters'"
                  [class.on]="source() === 'masters'" (click)="source.set('masters')">Masters</button>
        </div>
        <span class="spacer"></span>
        @if (source() === 'mine') {
          @if (tree(); as t) { <span class="hint" data-testid="tree-count">{{ t.gameCount }} {{ t.gameCount === 1 ? 'Game' : 'Games' }}</span> }
        } @else if (masters()?.available) {
          <span class="hint" data-testid="masters-count">{{ masters()!.gameCount.toLocaleString() }} Master Games</span>
        }
      </header>
      @if (source() === 'masters') {
        @if (masters(); as m) {
          @if (!m.available) {
            <p class="assessment-note" role="status" data-testid="masters-unavailable">{{ m.message }}</p>
          } @else if (m.moves.length === 0) {
            <p class="assessment-note">No Master Game reached this position.</p>
          } @else {
            <table class="tree-table">
              <thead><tr><th>Move</th><th class="num">Games</th><th>White · draw · Black</th></tr></thead>
              <tbody>
                @for (mv of m.moves; track mv.uci) {
                  <tr>
                    <td><button type="button" class="tree-move mono" data-testid="masters-move" (click)="play.emit(mv)"
                                [title]="'Show ' + mv.san + (mv.averageRating ? ' (average rating ' + mv.averageRating + ')' : '')">{{ mv.san }}</button></td>
                    <td class="num" data-testid="masters-games">{{ mv.gameCount.toLocaleString() }}</td>
                    <td>
                      <div class="score-cell">
                        <div class="score-bar result-bar" [title]="mv.whitePercent + '% White, ' + mv.drawPercent + '% draws, ' + mv.blackPercent + '% Black'">
                          <span class="white-won" [style.flex-grow]="mv.whitePercent"></span>
                          <span class="drawn" [style.flex-grow]="mv.drawPercent"></span>
                          <span class="black-won" [style.flex-grow]="mv.blackPercent"></span>
                        </div>
                        <span class="score mono" data-testid="masters-percent">{{ round(mv.whitePercent) }} · {{ round(mv.drawPercent) }} · {{ round(mv.blackPercent) }}</span>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
          @if (m.available && m.topGames.length) {
            <h3 class="tree-subhead">Top Master Games</h3>
            <ul class="position-games">
              @for (g of m.topGames; track g.id) {
                <li>
                  <button type="button" class="position-game master-game" data-testid="master-game" (click)="openMaster(g)"
                          title="Replay this Master Game (it is not added to your Collection)">
                    <span class="mono">{{ g.year ?? '' }}</span>
                    <span>{{ g.white }} <span class="hint">{{ g.whiteRating ?? '' }}</span> – {{ g.black }} <span class="hint">{{ g.blackRating ?? '' }}</span></span>
                    <span class="mono">{{ resultLabel(g.result) }}</span>
                  </button>
                </li>
              }
            </ul>
          }
        } @else {
          <p class="assessment-note">Asking Lichess…</p>
        }
      } @else if (error()) {
        <p class="assessment-note error" role="alert">{{ error() }}</p>
      } @else if (tree(); as t) {
        @if (t.moves.length) {
          <table class="tree-table">
            <thead><tr><th>Move</th><th class="num">Games</th><th>My score</th></tr></thead>
            <tbody>
              @for (m of t.moves; track m.uci) {
                <tr>
                  <td><button type="button" class="tree-move mono" data-testid="tree-move" (click)="play.emit(m)"
                              [title]="'Play ' + m.san">{{ m.san }}</button></td>
                  <td class="num" data-testid="tree-games">{{ m.gameCount }}</td>
                  <td>
                    <div class="score-cell">
                    <div class="score-bar" [title]="m.wins + ' won, ' + m.draws + ' drawn, ' + m.losses + ' lost'">
                      <span class="won" [style.flex-grow]="m.wins"></span>
                      <span class="drawn" [style.flex-grow]="m.draws"></span>
                      <span class="lost" [style.flex-grow]="m.losses"></span>
                    </div>
                    <span class="score mono" data-testid="tree-score">{{ m.score }}%</span>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else if (t.gameCount) {
          <p class="assessment-note">No Game went on from here.</p>
        } @else {
          <p class="assessment-note">None of your Games reached this position.</p>
        }

        @if (t.games.length) {
          <h3 class="tree-subhead">Games with this position</h3>
          <ul class="position-games">
            @for (p of shownGames(); track p.game.id) {
              <li>
                <button type="button" class="position-game" data-testid="tree-game" (click)="openGame.emit(p)"
                        [class.current]="p.game.id === game().id">
                  <span class="mono">{{ p.game.date ?? '' }}</span>
                  <span>{{ p.game.white }} – {{ p.game.black }}</span>
                  <span class="mono">{{ resultLabel(p.game.result) }}</span>
                  <span class="hint">move {{ p.moveNumber }}</span>
                </button>
              </li>
            }
          </ul>
          @if (t.games.length > shownGames().length) {
            <button type="button" class="link-button tree-more" (click)="showAll.set(true)">Show {{ t.games.length }}</button>
          } @else if (t.gameCount > t.games.length) {
            <p class="hint tree-more">The newest {{ t.games.length }} of {{ t.gameCount }}. Position Search lists more.</p>
          }
        }
      } @else {
        <p class="assessment-note">Loading…</p>
      }
    </section>
  `,
})
export class OpeningTreePanel {
  private readonly api = inject(Api);

  /** The position on the board. */
  readonly fen = input.required<string>();
  /** The open Game; a change to it (a move added, a line promoted) reloads the tree. */
  readonly game = input.required<Game>();
  /** A move from the tree (either source): the Game view steps into it, or shows it without saving. */
  readonly play = output<BoardMove>();
  readonly openGame = output<PositionGame>();

  private readonly router = inject(Router);

  /** Which statistics are shown: the Owner's Games or Master Games (Lichess). */
  protected readonly source = signal<'mine' | 'masters'>('mine');
  protected readonly tree = signal<OpeningTree | null>(null);
  protected readonly masters = signal<Masters | null>(null);
  protected readonly error = signal('');
  protected readonly showAll = signal(false);
  protected readonly resultLabel = resultLabel;
  protected readonly round = Math.round;
  protected readonly shownGames = computed(() => {
    const list = this.tree()?.games ?? [];
    return this.showAll() ? list : list.slice(0, 8);
  });

  /** Positions to load. Only the latest one's answer is shown, so a slow earlier answer never wins. */
  private readonly requests = new Subject<string>();

  constructor() {
    this.requests
      .pipe(
        debounceTime(120), // holding an arrow key doesn't ask for every position on the way
        switchMap((fen) =>
          this.api.openingTree(fen).pipe(
            map((tree) => ({ tree, error: '' })),
            catchError((e) => of({ tree: null, error: problemMessage(e) })),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ tree, error }) => {
        this.tree.set(tree);
        this.error.set(error);
        this.showAll.set(false);
      });
    // Masters are asked for only while their tab is open; the backend keeps answers for the session.
    this.mastersRequests
      .pipe(
        debounceTime(250),
        switchMap((fen) => this.api.masters(fen).pipe(catchError((e) => of(unavailable(problemMessage(e)))))),
        takeUntilDestroyed(),
      )
      .subscribe((masters) => this.masters.set(masters));

    effect(() => {
      this.game(); // a changed Game (a move added, a line promoted) reloads the tree too
      this.requests.next(this.fen());
    });
    effect(() => {
      if (this.source() !== 'masters') return;
      this.masters.set(null);
      this.mastersRequests.next(this.fen());
    });
  }

  private readonly mastersRequests = new Subject<string>();

  /** Replays a Master Game read-only, at the board's position. */
  protected openMaster(game: MasterGameRef) {
    this.router.navigate(['/masters', game.id], { queryParams: { fen: this.fen() } });
  }
}

function unavailable(message: string): Masters {
  return { available: false, message: `Masters unavailable: ${message}`, gameCount: 0, moves: [], topGames: [] };
}
