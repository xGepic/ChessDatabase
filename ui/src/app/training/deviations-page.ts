import { Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, OwnerDeviation, problemMessage } from '../api/api';
import { numberedLine } from '../engine/scores';
import { openGameAt } from '../shared/chess';

/**
 * The Games where the Owner left their own Repertoire, with the move played and the move the
 * Repertoire expected: the lines the Owner keeps forgetting. Filter by colour and date.
 */
@Component({
  selector: 'app-deviations-page',
  imports: [RouterLink],
  template: `
    <section class="card">
      <header class="card-head">
        <h1>My Deviations</h1>
        <span class="spacer"></span>
        <a routerLink="/repertoire/white">White Repertoire</a>
        <a routerLink="/repertoire/black">Black Repertoire</a>
      </header>
      <p class="hint card-body">Games where you left your own Repertoire. Click a row to open the Game at that move.</p>
      <form class="filters" aria-label="Filter the Deviations" (submit)="$event.preventDefault()">
        <label>Colour
          <select data-testid="deviations-colour" [value]="colour()" (change)="colour.set($any($event.target).value)">
            <option value="">Both</option>
            <option value="white">White</option>
            <option value="black">Black</option>
          </select>
        </label>
        <label>From <input class="date" data-testid="deviations-from" placeholder="YYYY[-MM[-DD]]" [value]="from()" (change)="from.set($any($event.target).value.trim())" /></label>
        <label>To <input class="date" data-testid="deviations-to" placeholder="YYYY[-MM[-DD]]" [value]="to()" (change)="to.set($any($event.target).value.trim())" /></label>
        @if (error()) { <span class="board-error" role="alert">{{ error() }}</span> }
      </form>
      @if (rows(); as list) {
        @if (list.length) {
          <table class="game-table" data-testid="deviations">
            <thead><tr><th>Date</th><th>Opponent</th><th>Colour</th><th>You played</th><th>Your Repertoire</th></tr></thead>
            <tbody>
              @for (d of list; track d.game.id) {
                <tr tabindex="0" data-testid="deviation-row" (click)="open(d)" (keydown.enter)="open(d)">
                  <td class="mono">{{ d.game.date ?? '' }}</td>
                  <td>{{ d.game.opponent.name }}</td>
                  <td>{{ d.game.ownerColour === 'white' ? 'White' : 'Black' }}</td>
                  <td class="mono">{{ numbered(d.ply, d.san) }}</td>
                  <td class="mono">{{ expected(d) }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="empty">{{ filtered() ? 'No Deviations with these filters.' : 'You never left your Repertoire. Well done.' }}</p>
        }
      } @else {
        <p class="empty">Loading…</p>
      }
    </section>
  `,
})
export class DeviationsPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly colour = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly rows = signal<OwnerDeviation[] | null>(null);
  protected readonly error = signal('');
  protected readonly filtered = computed(() => !!(this.colour() || this.from() || this.to()));

  constructor() {
    effect((onCleanup) => {
      const filters: Record<string, string> = {};
      if (this.colour()) filters['ownerColour'] = this.colour();
      if (this.from()) filters['from'] = this.from();
      if (this.to()) filters['to'] = this.to();
      const request = this.api.ownerDeviations(filters).subscribe({
        next: (rows) => {
          this.error.set('');
          this.rows.set(rows);
        },
        error: (e) => this.error.set(problemMessage(e)),
      });
      onCleanup(() => request.unsubscribe());
    });
  }

  protected numbered(ply: number, san: string): string {
    return numberedLine(ply, [san]);
  }

  protected expected(d: OwnerDeviation): string {
    return d.expected.map((san) => numberedLine(d.ply, [san])).join(' or ');
  }

  protected open(d: OwnerDeviation) {
    openGameAt(this.router, d.game.id, d.ply);
  }
}
