import { Component, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { Api, BoardMove, Tablebase, TablebaseVerdict, problemMessage } from '../api/api';

/** How each verdict reads: for the position (after "White"/"Black", except draws) and for a move. */
const VERDICTS: Record<TablebaseVerdict, { position: string; move: string }> = {
  win: { position: 'wins', move: 'Win' },
  cursedWin: { position: 'wins, but only after the 50-move rule', move: 'Win (50-move rule)' },
  maybeWin: { position: 'probably wins', move: 'Win?' },
  draw: { position: 'Draw', move: 'Draw' },
  unknown: { position: 'Unknown', move: 'Unknown' },
  maybeLoss: { position: 'probably loses', move: 'Loss?' },
  blessedLoss: { position: 'loses, but the 50-move rule saves', move: 'Loss (50-move rule)' },
  loss: { position: 'loses', move: 'Loss' },
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
                    <span class="tablebase-move-verdict">{{ verdicts[m.category].move }} <span class="hint">{{ distances(m.dtz, m.dtm) }}</span></span>
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

  protected readonly verdicts = VERDICTS;
  protected readonly result = signal<Tablebase | null>(null);
  /**
   * Whether the position is a Tablebase endgame: the backend decides (Chess Rules count the
   * pieces) and asks Lichess only then. Kept while the next answer loads, so the card doesn't flicker.
   */
  protected readonly applies = signal(false);
  private readonly requests = new Subject<string>();

  constructor() {
    this.requests
      .pipe(
        debounceTime(150),
        switchMap((fen) => this.api.tablebase(fen).pipe(catchError((e) => of(unavailable(problemMessage(e)))))),
        takeUntilDestroyed(),
      )
      .subscribe((r) => {
        this.applies.set(r.applicable);
        this.result.set(r);
      });
    effect(() => {
      const fen = this.fen();
      this.result.set(null);
      this.requests.next(fen);
    });
  }

  /** "White wins", "Draw", ... for the side to move. */
  protected verdict(r: Tablebase): string {
    const side = this.fen().split(' ')[1] === 'b' ? 'Black' : 'White';
    const category = r.category ?? 'unknown';
    const text = VERDICTS[category].position;
    return category === 'draw' || category === 'unknown' ? text : `${side} ${text}`;
  }

  /** "DTZ 3 · DTM 17": distances in half-moves, without signs. */
  protected distances(dtz: number | null, dtm: number | null): string {
    return [dtz ? `DTZ ${Math.abs(dtz)}` : null, dtm ? `DTM ${Math.abs(dtm)}` : null].filter((x) => x).join(' · ');
  }
}

function unavailable(message: string): Tablebase {
  return { applicable: true, available: false, message: `Tablebase unavailable: ${message}`, category: null, outcome: null, dtz: null, dtm: null, moves: [] };
}
