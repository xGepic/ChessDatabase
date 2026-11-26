import { Component, OnInit, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, PositionGame, problemMessage, resultLabel } from '../api/api';
import { PositionEditor } from './position-editor';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/**
 * Position Search: find every Game whose main line reached a position. The position comes from
 * a Game view ("Search this position", as ?fen=) or is set up in the editor. Each result opens the
 * Game at the matching position.
 */
@Component({
  selector: 'app-position-search',
  imports: [PositionEditor],
  template: `
    <div class="search-layout">
      <section class="card search-editor-card">
        <header class="card-head"><h1>Position Search</h1></header>
        <app-position-editor [initialFen]="startFen()" (fenChange)="current.set($event)" />
        <div class="search-actions">
          <button type="button" class="primary" data-testid="search-position" (click)="search()">Search my Games</button>
          @if (error()) { <span class="field-error" role="alert" data-testid="search-error">{{ error() }}</span> }
        </div>
      </section>

      <section class="card search-results-card" aria-live="polite">
        <header class="card-head">
          <h2>Games with this position</h2>
          <span class="spacer"></span>
          @if (results(); as r) { <span class="hint" data-testid="search-count">{{ r.length }} found</span> }
        </header>
        @if (results(); as r) {
          @if (r.length === 0) {
            <p class="empty" data-testid="search-empty">None of your Games reached this position in its main line.</p>
          } @else {
            <table class="game-table">
              <thead><tr><th>Date</th><th>White</th><th>Black</th><th>Result</th><th>Event</th><th class="num">Move</th></tr></thead>
              <tbody>
                @for (p of r; track p.game.id) {
                  <tr tabindex="0" data-testid="search-result" (click)="open(p)" (keydown.enter)="open(p)">
                    <td class="mono">{{ p.game.date ?? '' }}</td>
                    <td [class.owner]="p.game.ownerColour === 'white'">{{ p.game.white }}</td>
                    <td [class.owner]="p.game.ownerColour === 'black'">{{ p.game.black }}</td>
                    <td class="mono">{{ resultLabel(p.game.result) }}</td>
                    <td>{{ p.game.event?.name ?? '' }}</td>
                    <td class="num mono" data-testid="search-move">{{ p.moveNumber }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        } @else {
          <p class="empty">Set up a position and search, or use "Search this position" in a Game.</p>
        }
      </section>
    </div>
  `,
})
export class PositionSearch implements OnInit {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** Query parameter: the position to start with (and search at once). */
  readonly fen = input<string | undefined>();

  protected readonly current = signal(START);
  protected readonly results = signal<PositionGame[] | null>(null);
  protected readonly error = signal('');
  protected readonly resultLabel = resultLabel;
  protected readonly startFen = signal(START);

  /** Arriving with a position (from a Game view) searches it straight away. */
  ngOnInit() {
    const fen = this.fen();
    if (!fen) return;
    this.startFen.set(fen);
    this.current.set(fen);
    this.search();
  }

  protected search() {
    const fen = this.current();
    this.error.set('');
    this.api.searchPosition(fen).subscribe({
      next: (results) => {
        this.results.set(results);
        this.router.navigate([], { queryParams: { fen }, replaceUrl: true });
      },
      error: (e) => {
        this.results.set(null);
        this.error.set(problemMessage(e));
      },
    });
  }

  /** Opens the Game at the position that matched. */
  protected open(result: PositionGame) {
    this.router.navigate(['/games', result.game.id], { queryParams: { ply: result.ply } });
  }
}
