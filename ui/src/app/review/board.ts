import {
  Component,
  ElementRef,
  NO_ERRORS_SCHEMA,
  OnDestroy,
  afterNextRender,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Chessground } from '@lichess-org/chessground';
import type { Api as BoardApi } from '@lichess-org/chessground/api';
import type { Key } from '@lichess-org/chessground/types';
import { Chess, type Square } from 'chess.js';

type Promotion = 'q' | 'r' | 'b' | 'n';

/**
 * The board. chess.js only offers instant feedback on which moves are possible; the backend
 * decides what is saved (ADR 0002). Emits moves as UCI, including the promotion piece.
 */
@Component({
  selector: 'app-board',
  schemas: [NO_ERRORS_SCHEMA],
  template: `
    <div class="board-frame">
      <div class="board" #board data-testid="board"></div>
      @if (pendingPromotion(); as p) {
        <div class="promotion cg-wrap" role="dialog" aria-label="Promote to">
          @for (piece of promotionPieces; track piece.role) {
            <button type="button" [attr.data-testid]="'promote-' + piece.role" (click)="promote(piece.role)"
                    [attr.aria-label]="piece.name">
              <piece [class]="[piece.name, p.color]"></piece>
            </button>
          }
          <button type="button" class="promo-cancel" (click)="cancelPromotion()">Cancel</button>
        </div>
      }
    </div>
  `,
})
export class Board implements OnDestroy {
  readonly fen = input.required<string>();
  readonly orientation = input<'white' | 'black'>('white');
  readonly lastMove = input<string | null>(null);
  /** No moves can be made (e.g. a Master Game, which is only replayed); may change at any time. */
  readonly viewOnly = input(false);
  /** A move to draw as an arrow, in UCI (the Engine's best move); null draws none. */
  readonly arrow = input<string | null>(null);
  readonly move = output<string>();

  protected readonly promotionPieces: { role: Promotion; name: string }[] = [
    { role: 'q', name: 'queen' },
    { role: 'r', name: 'rook' },
    { role: 'b', name: 'bishop' },
    { role: 'n', name: 'knight' },
  ];
  protected readonly pendingPromotion = signal<{ from: string; to: string; color: 'white' | 'black' } | null>(null);

  private readonly boardEl = viewChild.required<ElementRef<HTMLElement>>('board');
  private cg: BoardApi | undefined;

  constructor() {
    afterNextRender(() => {
      this.cg = Chessground(this.boardEl().nativeElement, {
        coordinates: true,
        animation: { enabled: true, duration: 150 },
        movable: { free: false, showDests: true, events: { after: (from, to) => this.onBoardMove(from, to) } },
        draggable: { showGhost: true },
        premovable: { enabled: false },
        drawable: {
          enabled: true,
          brushes: { engine: { key: 'engine', color: '#1f6b4a', opacity: 0.85, lineWidth: 11 } } as never,
        },
      });
      this.sync();
    });
    effect(() => {
      // Track inputs and push them into chessground whenever they change.
      this.fen();
      this.orientation();
      this.lastMove();
      this.viewOnly();
      this.arrow();
      this.sync();
    });
  }

  ngOnDestroy() {
    this.cg?.destroy();
  }

  /** Puts the board back to the current position (e.g. after the backend refused a move). */
  reset() {
    this.pendingPromotion.set(null);
    this.sync();
  }

  private sync() {
    if (!this.cg) return;
    const chess = new Chess(this.fen());
    const turn = chess.turn() === 'w' ? 'white' : 'black';
    const dests = new Map<Key, Key[]>();
    for (const m of chess.moves({ verbose: true })) {
      const list = dests.get(m.from as Key) ?? [];
      if (!list.includes(m.to as Key)) list.push(m.to as Key);
      dests.set(m.from as Key, list);
    }
    const last = this.lastMove();
    this.cg.set({
      fen: this.fen(),
      orientation: this.orientation(),
      turnColor: turn,
      check: chess.inCheck(),
      lastMove: last ? [last.slice(0, 2) as Key, last.slice(2, 4) as Key] : undefined,
      // Not chessground's own viewOnly: that one can't be lifted after the board is created.
      movable: { color: this.viewOnly() ? undefined : turn, dests },
      selectable: { enabled: !this.viewOnly() },
      draggable: { enabled: !this.viewOnly(), showGhost: true },
    });
    const arrow = this.arrow();
    this.cg.setAutoShapes(arrow ? [{ orig: arrow.slice(0, 2) as Key, dest: arrow.slice(2, 4) as Key, brush: 'engine' }] : []);
  }

  private onBoardMove(from: Key, to: Key) {
    const chess = new Chess(this.fen());
    const piece = chess.get(from as Square);
    const lastRank = to[1] === '8' || to[1] === '1';
    if (piece?.type === 'p' && lastRank) {
      this.pendingPromotion.set({ from, to, color: piece.color === 'w' ? 'white' : 'black' });
      return;
    }
    this.move.emit(from + to);
  }

  protected promote(role: Promotion) {
    const p = this.pendingPromotion();
    if (!p) return;
    this.pendingPromotion.set(null);
    this.move.emit(p.from + p.to + role);
  }

  protected cancelPromotion() {
    this.reset();
  }
}
