import { Component, NO_ERRORS_SCHEMA, computed, input } from '@angular/core';

type Colour = 'white' | 'black';
type Role = 'queen' | 'rook' | 'bishop' | 'knight' | 'pawn';

const ROLES: { role: Role; letter: string; value: number }[] = [
  { role: 'queen', letter: 'q', value: 9 },
  { role: 'rook', letter: 'r', value: 5 },
  { role: 'bishop', letter: 'b', value: 3 },
  { role: 'knight', letter: 'n', value: 3 },
  { role: 'pawn', letter: 'p', value: 1 },
];

export interface MaterialSide {
  colour: Colour;
  /** The pieces this side has more of than the other, biggest first. */
  extra: Role[];
  /** Points ahead (queen 9, rook 5, bishop and knight 3, pawn 1); 0 when not ahead. */
  points: number;
}

/** The material balance of a FEN position: what each side has that the other doesn't, and who is ahead by how much. */
export function materialBalance(fen: string): MaterialSide[] {
  const placement = fen.split(' ')[0] ?? '';
  const count = (ch: string) => [...placement].filter((c) => c === ch).length;
  let score = 0; // from White's side
  const extra: Record<Colour, Role[]> = { white: [], black: [] };
  for (const { role, letter, value } of ROLES) {
    const diff = count(letter.toUpperCase()) - count(letter);
    score += diff * value;
    const side = diff > 0 ? extra.white : extra.black;
    for (let i = 0; i < Math.abs(diff); i++) side.push(role);
  }
  return [
    { colour: 'white', extra: extra.white, points: Math.max(0, score) },
    { colour: 'black', extra: extra.black, points: Math.max(0, -score) },
  ];
}

/**
 * Under the board: the pieces each side has more of than the other, and who is ahead by how many
 * points. Equal trades cancel out, so only the imbalance shows.
 */
@Component({
  selector: 'app-material-balance',
  schemas: [NO_ERRORS_SCHEMA], // <piece> is Chessground's element, styled by its piece set
  template: `
    <div class="material" data-testid="material" [attr.aria-label]="summary()">
      @for (side of sides(); track side.colour) {
        <div class="material-side" [class.black-side]="side.colour === 'black'" [attr.data-testid]="'material-' + side.colour">
          <span class="colour-dot" [class.black]="side.colour === 'black'"></span>
          <span class="material-pieces cg-wrap">
            @for (role of side.extra; track $index) { <piece [class]="side.colour + ' ' + role"></piece> }
          </span>
          @if (side.points) { <span class="material-points mono">+{{ side.points }}</span> }
        </div>
      }
      @if (even()) { <span class="material-even hint" data-testid="material-even">Material even</span> }
    </div>
  `,
})
export class MaterialBalance {
  readonly fen = input.required<string>();

  protected readonly sides = computed(() => materialBalance(this.fen()));
  protected readonly even = computed(() => this.sides().every((s) => !s.extra.length));
  protected readonly summary = computed(() => {
    if (this.even()) return 'Material even';
    const ahead = this.sides().find((s) => s.points);
    return ahead ? `${ahead.colour === 'white' ? 'White' : 'Black'} is ${ahead.points} up` : 'Material level in points, pieces differ';
  });
}
