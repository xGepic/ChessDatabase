import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { Api, Game, OpeningTree, PositionGame, TreeMove, problemMessage, resultLabel } from '../api/api';

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
        <span class="spacer"></span>
        @if (tree(); as t) { <span class="hint" data-testid="tree-count">{{ t.gameCount }} {{ t.gameCount === 1 ? 'Game' : 'Games' }}</span> }
      </header>
      @if (error()) {
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
  /** A move from the tree: the Game view steps into it, or shows it without saving. */
  readonly play = output<TreeMove>();
  readonly openGame = output<PositionGame>();

  protected readonly tree = signal<OpeningTree | null>(null);
  protected readonly error = signal('');
  protected readonly showAll = signal(false);
  protected readonly resultLabel = resultLabel;
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
    effect(() => {
      this.game(); // a changed Game (a move added, a line promoted) reloads the tree too
      this.requests.next(this.fen());
    });
  }
}
