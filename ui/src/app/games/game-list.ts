import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, combineLatest, map, of, startWith, switchMap, tap } from 'rxjs';
import { Api, GameSummary, eloLabel, problemMessage, resultLabel } from '../api/api';
import { AnalysisQueueService } from '../engine/analysis-queue';

/** The filter fields, as they appear in the URL and in the API's query parameters. */
const FILTER_KEYS = [
  'opponentId', 'eventId', 'from', 'to', 'result', 'ownerColour', 'ecoFrom', 'ecoTo', 'tag', 'timeControl', 'analysed',
];

interface Column {
  key: string;
  label: string;
  numeric?: boolean;
}

/**
 * The game list: sort by any column (click its header, again to reverse) and filter by
 * Opponent, Event, dates, result, colour, ECO range, Tag and time control. Filters and sort live
 * in the URL, so returning from a Game keeps them, and "Export to PGN" exports exactly what is shown.
 */
@Component({
  selector: 'app-game-list',
  imports: [RouterLink],
  templateUrl: './game-list.html',
})
export class GameList {
  constructor() {
    this.queue.watch();
    this.queue.finished.pipe(takeUntilDestroyed()).subscribe(() => this.reload.next());
  }

  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly queue = inject(AnalysisQueueService);
  /** Reloads the list, e.g. when a Game Analysis finishes and the "analysed" mark changes. */
  private readonly reload = new Subject<void>();

  /** Games ticked for Game Analysis. */
  protected readonly selected = signal<ReadonlySet<number>>(new Set());
  protected readonly queueError = signal('');

  protected readonly eloLabel = eloLabel;
  protected readonly resultLabel = resultLabel;
  protected readonly columns: Column[] = [
    { key: 'date', label: 'Date' },
    { key: 'white', label: 'White' },
    { key: 'whiteElo', label: 'Elo', numeric: true },
    { key: 'black', label: 'Black' },
    { key: 'blackElo', label: 'Elo', numeric: true },
    { key: 'result', label: 'Result' },
    { key: 'opponent', label: 'Opponent' },
    { key: 'ownerColour', label: 'Colour' },
    { key: 'event', label: 'Event' },
    { key: 'eco', label: 'ECO' },
  ];

  /** Current filters and sort from the URL. */
  private readonly params$ = this.route.queryParamMap.pipe(
    map((q) => Object.fromEntries(q.keys.map((k) => [k, q.get(k)!]).filter(([, v]) => v !== '')) as Record<string, string>),
  );
  protected readonly params = toSignal(this.params$, { initialValue: {} as Record<string, string> });
  /** Why the list could not be loaded, e.g. a date filter that is not a date. */
  protected readonly listError = signal('');
  protected readonly games = toSignal(
    combineLatest([this.params$, this.reload.pipe(startWith(undefined))]).pipe(
      switchMap(([params]) =>
        this.api.listGames(params).pipe(
          tap(() => this.listError.set('')),
          catchError((e) => {
            this.listError.set(problemMessage(e));
            return of([] as GameSummary[]);
          }),
        ),
      ),
    ),
  );
  protected readonly filtered = computed(() => FILTER_KEYS.some((k) => this.params()[k]));
  protected readonly exportUrl = computed(() => {
    const query = new URLSearchParams(this.params()).toString();
    return query ? `/api/games/pgn?${query}` : '/api/games/pgn';
  });

  protected readonly players = toSignal(this.api.searchPlayers(''), { initialValue: [] });
  protected readonly events = toSignal(this.api.searchEvents(''), { initialValue: [] });
  protected readonly tags = toSignal(this.api.searchTags(''), { initialValue: [] });

  protected value(key: string): string {
    return this.params()[key] ?? '';
  }

  protected setFilter(key: string, value: string) {
    this.navigate({ [key]: value.trim() || null });
  }

  protected clearFilters() {
    this.navigate(Object.fromEntries(FILTER_KEYS.map((k) => [k, null])));
  }

  /** First click sorts ascending (dates: newest first), the next click reverses. */
  protected sortBy(column: Column) {
    const current = this.params()['sort'];
    const direction = this.params()['direction'];
    const firstDirection = column.key === 'date' ? 'desc' : 'asc';
    const next = current === column.key ? (direction === 'asc' ? 'desc' : 'asc') : firstDirection;
    this.navigate({ sort: column.key, direction: next });
  }

  protected sortMark(column: Column): string {
    if (this.params()['sort'] !== column.key) return '';
    return this.params()['direction'] === 'desc' ? ' ▼' : ' ▲';
  }

  protected ariaSort(column: Column): string | null {
    if (this.params()['sort'] !== column.key) return null;
    return this.params()['direction'] === 'desc' ? 'descending' : 'ascending';
  }

  protected isSelected(id: number): boolean {
    return this.selected().has(id);
  }

  protected toggle(id: number) {
    const next = new Set(this.selected());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.selected.set(next);
  }

  /** Ticks every Game shown, or clears the ticks when all are ticked already. */
  protected toggleAll(list: GameSummary[]) {
    const all = list.length > 0 && list.every((g) => this.selected().has(g.id));
    this.selected.set(all ? new Set() : new Set(list.map((g) => g.id)));
  }

  protected allSelected(list: GameSummary[]): boolean {
    return list.length > 0 && list.every((g) => this.selected().has(g.id));
  }

  /** Queues the ticked Games in list order. */
  protected analyseSelected(list: GameSummary[]) {
    const ids = list.filter((g) => this.selected().has(g.id)).map((g) => g.id);
    if (!ids.length) return;
    this.queueError.set('');
    this.api.queueAnalysis(ids).subscribe({
      next: (status) => {
        this.queue.set(status);
        this.selected.set(new Set());
      },
      error: (e) => this.queueError.set(problemMessage(e)),
    });
  }

  protected open(game: GameSummary) {
    this.router.navigate(['/games', game.id]);
  }

  private navigate(queryParams: Record<string, string | null>) {
    this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
