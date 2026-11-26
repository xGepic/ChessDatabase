import { Component, computed, input, output } from '@angular/core';
import { Game, MoveNode, resultLabel } from '../api/api';

interface Row {
  number: number;
  white: MoveNode | null;
  black: MoveNode | null;
}

/** The notation as a scoresheet: numbered rows with White and Black columns. */
@Component({
  selector: 'app-scoresheet',
  template: `
    <div class="sheet" data-testid="notation">
      @for (row of rows(); track row.number) {
        <div class="sheet-row">
          <span class="move-no">{{ row.number }}.</span>
          @if (row.white; as w) {
            <button type="button" class="move" [class.current]="w.id === currentId()" (click)="select.emit(w.id)"
                    data-testid="move">{{ w.san }}</button>
          } @else {
            <span class="move ellipsis">…</span>
          }
          @if (row.black; as b) {
            <button type="button" class="move" [class.current]="b.id === currentId()" (click)="select.emit(b.id)"
                    data-testid="move">{{ b.san }}</button>
          } @else {
            <span class="move"></span>
          }
        </div>
      } @empty {
        <p class="sheet-empty">Play the moves on the board.</p>
      }
      @if (rows().length) {
        <div class="sheet-end">{{ resultLabel(game().result) }}</div>
      }
    </div>
  `,
})
export class Scoresheet {
  readonly game = input.required<Game>();
  readonly currentId = input<number | null>(null);
  readonly select = output<number | null>();
  protected readonly resultLabel = resultLabel;

  protected readonly rows = computed<Row[]>(() => {
    const game = this.game();
    const byId = new Map(game.nodes.map((n) => [n.id, n]));
    const rows: Row[] = [];
    let node = byId.get(game.firstMoves[0]);
    while (node) {
      const number = Math.ceil(node.ply / 2);
      let row = rows[rows.length - 1];
      if (!row || row.number !== number) rows.push((row = { number, white: null, black: null }));
      if (node.ply % 2 === 1) row.white = node;
      else row.black = node;
      node = byId.get(node.children[0]);
    }
    return rows;
  });
}
