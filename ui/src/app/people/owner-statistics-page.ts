import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { Api, OwnerStatistics, problemMessage } from '../api/api';
import { TimeChart, accuracyPoints, ratingPoints } from '../shared/time-chart';
import { ScoreBar } from './score-bar';

const FILTERS = ['from', 'to'] as const;

/**
 * My statistics, the Owner's own figures: score overall, by colour and by opening, rating history, average
 * opponent rating and Accuracy over time. One date range applies to every figure.
 */
@Component({
  selector: 'app-owner-statistics-page',
  imports: [TimeChart, ScoreBar],
  template: `
    <div class="stats">
      <section class="card stats-wide">
        <header class="card-head"><h1>My statistics</h1></header>
        <form class="filters" aria-label="Limit every figure" (submit)="$event.preventDefault()">
          <label>From <input class="date" data-testid="stats-from" placeholder="YYYY[-MM[-DD]]" [value]="value('from')"
                              (change)="setFilter('from', $any($event.target).value)" /></label>
          <label>To <input class="date" data-testid="stats-to" placeholder="YYYY[-MM[-DD]]" [value]="value('to')"
                            (change)="setFilter('to', $any($event.target).value)" /></label>
          @if (filtered()) { <button type="button" class="link-button" (click)="clear()">Clear filters</button> }
        </form>
        @if (error()) { <p class="field-error filter-error" role="alert">{{ error() }}</p> }
      </section>

      @if (stats(); as s) {
        <section class="card">
          <header class="card-head"><h2>Score</h2><span class="spacer"></span><span class="hint">{{ s.score.games }} Games</span></header>
          <div class="score-rows">
            @for (row of [{ label: 'Overall', score: s.score }, { label: 'With White', score: s.white }, { label: 'With Black', score: s.black }]; track row.label) {
              <span>{{ row.label }}</span>
              <app-score-bar [score]="row.score" />
              <span class="mono" [attr.data-testid]="'stats-' + row.label">{{ row.score.games ? row.score.percent + '%' : '–' }}</span>
              <span class="hint">{{ row.score.wins }}/{{ row.score.draws }}/{{ row.score.losses }}</span>
            }
          </div>
          <div class="stat-tile">
            <span class="hint">Average opponent rating</span>
            <strong class="mono" data-testid="stats-opponent-elo">{{ s.averageOpponentElo ?? '–' }}</strong>
            <span class="hint">over {{ s.ratedOpponentGames }} rated {{ s.ratedOpponentGames === 1 ? 'Opponent' : 'Opponents' }}; unrated ones left out</span>
          </div>
        </section>

        <section class="card">
          <header class="card-head"><h2>My rating</h2></header>
          <div class="card-body">
            <app-time-chart [points]="rating()" label="My rating history" valueName="Elo"
                            emptyText="No Games with your Elo and a date." (open)="openGame($event)" />
          </div>
        </section>

        <section class="card">
          <header class="card-head"><h2>Accuracy over time</h2></header>
          <div class="card-body" data-testid="stats-accuracy">
            <app-time-chart [points]="accuracy()" label="My Accuracy over time" valueName="Accuracy" [decimals]="1"
                            emptyText="No analysed Games with a date. Analyse Games to see your Accuracy here." (open)="openGame($event)" />
          </div>
        </section>

        <section class="card stats-wide">
          <header class="card-head"><h2>By opening</h2></header>
          <table class="game-table" data-testid="stats-openings">
            <thead><tr><th>ECO</th><th>Opening</th><th>I played</th><th class="num">Games</th><th>My score</th></tr></thead>
            <tbody>
              @for (o of s.openings; track (o.eco ?? '') + o.ownerColour) {
                <tr>
                  <td class="mono">{{ o.eco ?? '–' }}</td>
                  <td>{{ o.name ?? 'No moves' }}</td>
                  <td><span class="colour-dot" [class.black]="o.ownerColour === 'black'"></span>{{ o.ownerColour === 'white' ? 'White' : 'Black' }}</td>
                  <td class="num">{{ o.score.games }}</td>
                  <td><div class="score-cell"><app-score-bar [score]="o.score" /><span class="score mono">{{ o.score.percent }}%</span></div></td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="hint">No Games match these filters.</td></tr>
              }
            </tbody>
          </table>
        </section>
      } @else if (!error()) {
        <p class="empty">Loading…</p>
      }
    </div>
  `,
})
export class OwnerStatisticsPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly stats = signal<OwnerStatistics | null>(null);
  protected readonly error = signal('');
  private readonly params = signal<Record<string, string>>({});
  protected readonly filtered = computed(() => FILTERS.some((k) => this.params()[k]));
  protected readonly rating = computed(() => ratingPoints(this.stats()?.ratingHistory));
  protected readonly accuracy = computed(() => accuracyPoints(this.stats()?.accuracy));

  constructor() {
    this.route.queryParamMap
      .pipe(
        map((q) => Object.fromEntries(FILTERS.map((k) => [k, q.get(k) ?? '']).filter(([, v]) => v !== ''))),
        switchMap((params) => {
          this.params.set(params);
          return this.api.ownerStatistics(params).pipe(
            map((stats) => ({ stats, error: '' })),
            catchError((e) => of({ stats: null, error: problemMessage(e) })),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe(({ stats, error }) => {
        this.stats.set(stats);
        this.error.set(error);
      });
  }

  protected value(key: (typeof FILTERS)[number]): string {
    return this.params()[key] ?? '';
  }

  protected setFilter(key: (typeof FILTERS)[number], value: string) {
    this.router.navigate([], { queryParams: { [key]: value.trim() || null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected clear() {
    this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  protected openGame(id: number) {
    this.router.navigate(['/games', id]);
  }
}
