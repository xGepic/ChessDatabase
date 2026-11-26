import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { Api, BoardMove, Tablebase, TablebaseMove, problemMessage } from '../api/api';

/** The largest endgame the tablebase covers, kings included. */
const MAX_PIECES = 7;

const VERDICTS: Record<string, string> = {
  win: 'wins',
  loss: 'loses',
  draw: 'Draw',
  'cursed-win': 'wins, but only after the 50-move rule',
  'blessed-loss': 'loses, but the 50-move rule saves',
  'maybe-win': 'probably wins',
  'maybe-loss': 'probably loses',
  unknown: 'Unknown',
};

/**
 * The exact Tablebase verdict for endgames with up to 7 pieces, and the moves ranked best first.
 * Shown only for such positions; when Lichess can't be reached it says so quietly.
 */
@Component({
  selector: 'app-tablebase-panel',
  template: `
    @if (applies()) {
      <section class="card tablebase-card" aria-label="Tablebase" data-testid="tablebase">
        <header class="engine-head">
          <span class="engine-title">Tablebase</span>
          @if (result(); as r) {
            @if (r.available) {
              <span class="tablebase-verdict" data-testid="tablebase-verdict" [attr.data-category]="r.category">{{ verdict(r) }}</span>
              <span class="engine-depth">{{ distances(r.dtz, r.dtm) }}</span>
            }
          }
        </header>
        @if (result(); as r) {
          @if (!r.available) {
            <p class="engine-message" data-testid="tablebase-unavailable">{{ r.message }}</p>
          } @else {
            <ol class="engine-lines">
              @for (m of r.moves; track m.uci) {
                <li>
                  <button type="button" class="engine-line tablebase-move" data-testid="tablebase-move" [attr.data-category]="m.category"
                          (click)="play.emit(m)" [title]="'Show ' + m.san">
                    <span class="mono">{{ m.san }}</span>
                    <span class="tablebase-move-verdict">{{ moveVerdict(m) }} <span class="hint">{{ distances(m.dtz, m.dtm) }}</span></span>
                  </button>
                </li>
              }
            </ol>
          }
        } @else {
          <p class="engine-message">Looking it up…</p>
        }
      </section>
    }
  `,
})
export class TablebasePanel {
  private readonly api = inject(Api);

  /** The position on the board. */
  readonly fen = input.required<string>();
  /** A move from the list: the Game view steps into it, or shows it without saving. */
  readonly play = output<BoardMove>();

  protected readonly result = signal<Tablebase | null>(null);
  /** Only endgames with few enough pieces are looked up; counted from the FEN's board. */
  protected readonly applies = computed(() => (this.fen().split(' ')[0].match(/[a-z]/gi) ?? []).length <= MAX_PIECES);
  private readonly requests = new Subject<string | null>();

  constructor() {
    this.requests
      .pipe(
        debounceTime(150),
        switchMap((fen) =>
          fen === null
            ? EMPTY
            : this.api.tablebase(fen).pipe(catchError((e) => of(unavailable(problemMessage(e))))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((r) => this.result.set(r));
    effect(() => {
      const fen = this.fen();
      this.result.set(null);
      this.requests.next(this.applies() ? fen : null);
    });
  }

  /** "White wins", "Draw", ... for the side to move. */
  protected verdict(r: Tablebase): string {
    const side = this.fen().split(' ')[1] === 'b' ? 'Black' : 'White';
    const text = VERDICTS[r.category ?? 'unknown'] ?? 'Unknown';
    return r.category === 'draw' || r.category === 'unknown' || !r.category ? text : `${side} ${text}`;
  }

  protected moveVerdict(m: TablebaseMove): string {
    return { win: 'Win', loss: 'Loss', draw: 'Draw', 'cursed-win': 'Win (50-move rule)', 'blessed-loss': 'Loss (50-move rule)',
      'maybe-win': 'Win?', 'maybe-loss': 'Loss?' }[m.category] ?? 'Unknown';
  }

  /** "DTZ 3 · DTM 17": distances in half-moves, without signs. */
  protected distances(dtz: number | null, dtm: number | null): string {
    return [dtz ? `DTZ ${Math.abs(dtz)}` : null, dtm ? `DTM ${Math.abs(dtm)}` : null].filter((x) => x).join(' · ');
  }
}

function unavailable(message: string): Tablebase {
  return { applicable: true, available: false, message: `Tablebase unavailable: ${message}`, category: null, outcome: null, dtz: null, dtm: null, moves: [] };
}
