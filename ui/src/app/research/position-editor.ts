import {
  Component,
  ElementRef,
  NO_ERRORS_SCHEMA,
  OnDestroy,
  afterNextRender,
  computed,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Chessground } from '@lichess-org/chessground';
import type { Api as BoardApi } from '@lichess-org/chessground/api';
import { dragNewPiece } from '@lichess-org/chessground/drag';
import type { Color, Key, Piece, Role } from '@lichess-org/chessground/types';
import { INITIAL_FEN } from '../shared/chess';
const ROLES: Role[] = ['king', 'queen', 'rook', 'bishop', 'knight', 'pawn'];
const CASTLING = [
  { flag: 'K', label: 'White O-O' },
  { flag: 'Q', label: 'White O-O-O' },
  { flag: 'k', label: 'Black O-O' },
  { flag: 'q', label: 'Black O-O-O' },
];

/** A palette choice: a piece to place, or the eraser. */
type Tool = Piece | 'erase';

/**
 * Sets up a position: drag pieces around, drag them off the board to remove them, and place new
 * ones from the palette (click a palette piece, then squares; or drag it on). Side to move and
 * castling rights complete the position. Whether it can occur is checked by the backend (ADR 0002).
 */
@Component({
  selector: 'app-position-editor',
  schemas: [NO_ERRORS_SCHEMA],
  template: `
    <div class="editor">
      <div class="palette" role="toolbar" aria-label="Black pieces">
        @for (role of roles; track role) {
          <button type="button" class="palette-piece cg-wrap" [class.on]="isTool('black', role)" [attr.aria-label]="'Black ' + role"
                  [attr.data-testid]="'palette-black-' + role" (click)="choose({ color: 'black', role })"
                  (mousedown)="startDrag($event, { color: 'black', role })">
            <piece [class]="'black ' + role"></piece>
          </button>
        }
      </div>
      <div class="board-frame">
        <div class="board" #board data-testid="editor-board"></div>
      </div>
      <div class="palette" role="toolbar" aria-label="White pieces">
        @for (role of roles; track role) {
          <button type="button" class="palette-piece cg-wrap" [class.on]="isTool('white', role)" [attr.aria-label]="'White ' + role"
                  [attr.data-testid]="'palette-white-' + role" (click)="choose({ color: 'white', role })"
                  (mousedown)="startDrag($event, { color: 'white', role })">
            <piece [class]="'white ' + role"></piece>
          </button>
        }
        <button type="button" class="palette-erase" [class.on]="tool() === 'erase'" data-testid="palette-erase"
                (click)="choose('erase')" title="Click squares to empty them">Erase</button>
      </div>

      <div class="editor-options">
        <div class="segmented" role="radiogroup" aria-label="Side to move">
          <button type="button" role="radio" data-testid="editor-white-to-move" [attr.aria-checked]="side() === 'w'"
                  [class.on]="side() === 'w'" (click)="side.set('w')">White to move</button>
          <button type="button" role="radio" data-testid="editor-black-to-move" [attr.aria-checked]="side() === 'b'"
                  [class.on]="side() === 'b'" (click)="side.set('b')">Black to move</button>
        </div>
        <div class="castling" role="group" aria-label="Castling rights">
          @for (c of castlingOptions; track c.flag) {
            <label><input type="checkbox" [checked]="castling().includes(c.flag)" (change)="toggleCastling(c.flag)" /> {{ c.label }}</label>
          }
        </div>
        <div class="editor-actions">
          <button type="button" data-testid="editor-start" (click)="setUp(startFen)">Start position</button>
          <button type="button" data-testid="editor-clear" (click)="setUp('8/8/8/8/8/8/8/8 w - - 0 1')">Clear board</button>
          <button type="button" (click)="flipped.set(!flipped())">Flip</button>
        </div>
      </div>
    </div>
  `,
})
export class PositionEditor implements OnDestroy {
  /** The position to start from; setting it again replaces whatever is on the board. */
  readonly initialFen = input<string>(INITIAL_FEN);
  /** The position on the editor as a FEN, every time it changes. */
  readonly fenChange = output<string>();

  protected readonly roles = ROLES;
  protected readonly castlingOptions = CASTLING;
  protected readonly startFen = INITIAL_FEN;
  protected readonly tool = signal<Tool | null>(null);
  protected readonly side = signal<'w' | 'b'>('w');
  protected readonly castling = signal('KQkq');
  protected readonly flipped = signal(false);
  private readonly board = signal(INITIAL_FEN.split(' ')[0]);
  /** The en passant square of the starting FEN, kept until the Owner changes anything. */
  private readonly enPassant = signal('-');

  private readonly boardEl = viewChild.required<ElementRef<HTMLElement>>('board');
  private cg: BoardApi | undefined;

  protected readonly fen = computed(
    () => `${this.board()} ${this.side()} ${this.castling() || '-'} ${this.enPassant()} 0 1`,
  );

  constructor() {
    afterNextRender(() => {
      this.cg = Chessground(this.boardEl().nativeElement, {
        coordinates: true,
        animation: { enabled: false },
        movable: { free: true, color: 'both', showDests: false },
        draggable: { showGhost: true, deleteOnDropOff: true },
        premovable: { enabled: false },
        drawable: { enabled: false },
        highlight: { lastMove: false, check: false },
        events: {
          change: () => this.readBoard(),
          select: (key) => this.onSquare(key),
        },
      });
      this.setUp(this.initialFen());
    });
    effect(() => this.setUp(this.initialFen()));
    effect(() => this.cg?.set({ orientation: this.flipped() ? 'black' : 'white' }));
    effect(() => this.fenChange.emit(this.fen()));
  }

  ngOnDestroy() {
    this.cg?.destroy();
  }

  protected isTool(color: Color, role: Role): boolean {
    const tool = this.tool();
    return tool !== null && tool !== 'erase' && tool.color === color && tool.role === role;
  }

  /** Clicking the chosen tool again goes back to moving pieces. */
  protected choose(tool: Tool) {
    const current = this.tool();
    const same = current === tool || (current !== null && current !== 'erase' && tool !== 'erase'
      && current.color === tool.color && current.role === tool.role);
    this.tool.set(same ? null : tool);
  }

  protected startDrag(event: MouseEvent, piece: Piece) {
    if (!this.cg || event.button !== 0) return;
    dragNewPiece(this.cg.state, piece, event, true);
  }

  protected toggleCastling(flag: string) {
    const flags = this.castling().includes(flag) ? this.castling().replace(flag, '') : this.castling() + flag;
    this.castling.set('KQkq'.split('').filter((f) => flags.includes(f)).join(''));
    this.enPassant.set('-');
  }

  /** Puts a whole FEN on the editor. */
  protected setUp(fen: string) {
    const [board, side, castling, enPassant] = fen.split(' ');
    this.board.set(board);
    this.side.set(side === 'b' ? 'b' : 'w');
    this.castling.set(castling && castling !== '-' ? castling : '');
    this.enPassant.set(enPassant ?? '-');
    this.cg?.set({ fen: board, lastMove: undefined });
  }

  private onSquare(key: Key) {
    const tool = this.tool();
    if (!this.cg || tool === null) return;
    this.cg.setPieces(new Map([[key, tool === 'erase' ? undefined : { ...tool }]]));
    this.cg.selectSquare(null);
    this.readBoard();
  }

  private readBoard() {
    if (!this.cg) return;
    const board = this.cg.getFen();
    if (board === this.board()) return;
    this.board.set(board);
    this.enPassant.set('-');
  }
}
