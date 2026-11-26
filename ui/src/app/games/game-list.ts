import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, map, of, switchMap, tap } from 'rxjs';
import { Api, GameSummary, eloLabel, problemMessage, resultLabel } from '../api/api';

/** The filter fields, as they appear in the URL and in the API's query parameters. */
const FILTER_KEYS = ['opponentId', 'eventId', 'from', 'to', 'result', 'colour', 'ecoFrom', 'ecoTo', 'tag', 'timeControl'];

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
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

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
    { key: 'colour', label: 'Colour' },
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
    this.params$.pipe(
      switchMap((params) =>
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

  protected open(game: GameSummary) {
    this.router.navigate(['/games', game.id]);
  }

  private navigate(queryParams: Record<string, string | null>) {
    this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }
}
