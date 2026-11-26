import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, startWith, switchMap } from 'rxjs';
import { Api, PlayerRow } from '../api/api';

/** Every Player the Owner has played, with the Owner's score against them; each opens its Dossier. */
@Component({
  selector: 'app-players-page',
  imports: [RouterLink],
  template: `
    <section class="card list-card">
      <header class="card-head">
        <h1>Players</h1>
        @if (players(); as list) { <span class="hint" data-testid="player-count">{{ list.length }} shown</span> }
        <span class="spacer"></span>
        <input class="player-search" type="search" placeholder="Search by name" aria-label="Search Players" data-testid="player-search"
               (input)="search.next($any($event.target).value)" />
      </header>
      @if (players(); as list) {
        @if (list.length === 0) {
          <p class="empty">No Players found.</p>
        } @else {
          <table class="game-table">
            <thead><tr><th>Name</th><th>Club</th><th class="num">Games</th><th class="num">My score</th><th>Last Game</th><th></th></tr></thead>
            <tbody>
              @for (p of list; track p.id) {
                <tr tabindex="0" data-testid="player-row" (click)="open(p)" (keydown.enter)="open(p)">
                  <td><a [routerLink]="['/players', p.id]" (click)="$event.stopPropagation()">{{ p.name }}</a></td>
                  <td>{{ p.club ?? '' }}</td>
                  <td class="num">{{ p.score.games }}</td>
                  <td class="num mono">{{ p.score.games ? p.score.percent + '%' : '' }}</td>
                  <td class="mono">{{ p.lastDate ?? '' }}</td>
                  <td><a [routerLink]="['/players', p.id]" [queryParams]="{ merge: 1 }" (click)="$event.stopPropagation()"
                         data-testid="player-merge" title="Merge another Player into this one">Merge…</a></td>
                </tr>
              }
            </tbody>
          </table>
        }
      } @else {
        <p class="empty">Loading…</p>
      }
    </section>
  `,
})
export class PlayersPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  protected readonly search = new Subject<string>();
  protected readonly players = signal<PlayerRow[] | null>(null);

  constructor() {
    this.search
      .pipe(debounceTime(150), startWith(''), switchMap((term) => this.api.playerOverview(term)), takeUntilDestroyed())
      .subscribe((list) => this.players.set(list));
  }

  protected open(player: PlayerRow) {
    this.router.navigate(['/players', player.id]);
  }
}
