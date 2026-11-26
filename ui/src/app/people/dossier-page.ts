import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Subject, catchError, combineLatest, of, startWith, switchMap } from 'rxjs';
import { Api, Dossier, problemMessage, resultLabel } from '../api/api';
import { TimeChart } from '../shared/time-chart';
import { MergePanel } from './merge-panel';
import { ScoreBar } from './score-bar';

/**
 * A Player's Dossier: the Owner's head-to-head score, the Player's rating history (from the
 * Elo recorded in the Owner's Games), the openings they met the Owner with, and every Game.
 */
@Component({
  selector: 'app-dossier-page',
  imports: [RouterLink, TimeChart, ScoreBar, MergePanel],
  template: `
    @if (dossier(); as d) {
      <div class="dossier">
        <section class="card dossier-head">
          <header class="card-head">
            <h1 data-testid="dossier-name">{{ d.player.name }}</h1>
            <span class="spacer"></span>
            <button type="button" class="link-button" data-testid="merge-open" (click)="merging.set(true)">Merge…</button>
            <a routerLink="/players">All Players</a>
          </header>
          <div class="form-grid compact">
            <label for="player-fide">FIDE ID</label>
            <input id="player-fide" class="elo" data-testid="player-fide" inputmode="numeric" [value]="d.player.fideId ?? ''"
                   (change)="saveDetails(fide.value, club.value)" #fide />
            <label for="player-club">Club</label>
            <input id="player-club" data-testid="player-club" [value]="d.player.club ?? ''" (change)="saveDetails(fide.value, club.value)" #club />
            <span></span>
            <p class="hint">@if (status()) { <span class="status" data-testid="player-status">{{ status() }}</span> }
              @if (d.player.fideId) { <a [href]="'https://ratings.fide.com/profile/' + d.player.fideId" target="_blank" rel="noopener">FIDE profile</a> }</p>
          </div>
          <div class="head-to-head" data-testid="head-to-head">
            <span class="big-score mono" data-testid="dossier-score">{{ d.score.percent }}%</span>
            <app-score-bar [score]="d.score" />
            <span class="hint">{{ d.score.wins }} won · {{ d.score.draws }} drawn · {{ d.score.losses }} lost, from your side</span>
          </div>
        </section>

        @if (merging()) {
          <app-merge-panel class="dossier-wide" [player]="d.player" (merged)="onMerged()" (closed)="merging.set(false)" />
        }
        <section class="card">
          <header class="card-head"><h2>Rating history</h2></header>
          <div class="card-body">
            <app-time-chart [points]="rating()" label="Rating history" valueName="Elo"
                            emptyText="No rated Games with a date against this Player." (open)="openGame($event)" />
          </div>
        </section>

        <section class="card">
          <header class="card-head"><h2>Openings</h2></header>
          <table class="game-table" data-testid="dossier-openings">
            <thead><tr><th>ECO</th><th>Opening</th><th class="num">Games</th><th>My score</th></tr></thead>
            <tbody>
              @for (o of d.openings; track o.eco) {
                <tr>
                  <td class="mono">{{ o.eco ?? '–' }}</td>
                  <td>{{ o.name ?? 'No moves' }}</td>
                  <td class="num">{{ o.score.games }}</td>
                  <td><div class="score-cell"><app-score-bar [score]="o.score" /><span class="score mono">{{ o.score.percent }}%</span></div></td>
                </tr>
              }
            </tbody>
          </table>
        </section>

        <section class="card">
          <header class="card-head"><h2>Games</h2></header>
          <table class="game-table" data-testid="dossier-games">
            <thead><tr><th>Date</th><th>White</th><th>Black</th><th>Result</th><th>Event</th><th>ECO</th></tr></thead>
            <tbody>
              @for (g of d.games; track g.id) {
                <tr tabindex="0" data-testid="dossier-game" (click)="openGame(g.id)" (keydown.enter)="openGame(g.id)">
                  <td class="mono">{{ g.date ?? '' }}</td>
                  <td [class.owner]="g.ownerColour === 'white'">{{ g.white }}{{ g.whiteElo ? ' (' + g.whiteElo + ')' : '' }}</td>
                  <td [class.owner]="g.ownerColour === 'black'">{{ g.black }}{{ g.blackElo ? ' (' + g.blackElo + ')' : '' }}</td>
                  <td class="mono">{{ resultLabel(g.result) }}</td>
                  <td>{{ g.event?.name ?? '' }}</td>
                  <td class="mono">{{ g.eco ?? '' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      </div>
    } @else if (error()) {
      <p class="empty" role="alert">{{ error() }}</p>
    } @else {
      <p class="empty">Loading…</p>
    }
  `,
})
export class DossierPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** Route parameter: the Player's id. */
  readonly id = input.required<string>();
  /** Query parameter: open the merge panel straight away (from the Players list). */
  readonly merge = input<string | undefined>();

  protected readonly merging = signal(false);
  private readonly reload = new Subject<void>();

  protected readonly dossier = signal<Dossier | null>(null);
  protected readonly error = signal('');
  protected readonly status = signal('');
  protected readonly resultLabel = resultLabel;
  protected readonly rating = computed(() =>
    (this.dossier()?.ratingHistory ?? []).map((p) => ({ date: p.date, value: p.elo, id: p.gameId })),
  );

  constructor() {
    combineLatest([toObservable(this.id), this.reload.pipe(startWith(undefined))])
      .pipe(
        switchMap(([id]) => this.api.dossier(Number(id)).pipe(catchError((e) => {
          this.error.set(problemMessage(e));
          return of(null);
        }))),
        takeUntilDestroyed(),
      )
      .subscribe((d) => this.dossier.set(d));
    effect(() => this.merging.set(this.merge() !== undefined));
  }

  /** The other Player is gone and its Games are here now. */
  protected onMerged() {
    this.merging.set(false);
    this.status.set('Merged');
    this.reload.next();
  }

  protected saveDetails(fideId: string, club: string) {
    const d = this.dossier();
    if (!d) return;
    this.api.updatePlayer(d.player.id, { name: d.player.name, fideId, club }).subscribe({
      next: () => {
        this.status.set('Saved');
        this.dossier.set({ ...d, player: { ...d.player, fideId: fideId.trim() || null, club: club.trim() || null } });
      },
      error: (e) => this.status.set(problemMessage(e)),
    });
  }

  protected openGame(id: number) {
    this.router.navigate(['/games', id]);
  }
}
