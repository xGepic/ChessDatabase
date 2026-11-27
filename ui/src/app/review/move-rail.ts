import { Component, computed, input, output } from '@angular/core';
import { Classification, MoveNode, MoveTreeView } from '../api/api';
import { moveLabel } from './notation';

/**
 * The main line as a row of markers under the board, one per move, coloured by the Engine's
 * Move Classification. The move on the board is ringed; clicking a marker goes to that move.
 */
@Component({
  selector: 'app-move-rail',
  template: `
    @if (moves().length) {
      <nav class="move-rail" aria-label="Main line" data-testid="move-rail" [style.--moves]="moves().length + 1">
        <button type="button" class="rail-start" [class.current]="currentId() === null" (click)="select.emit(null)"
                title="Starting position" aria-label="Starting position"></button>
        @for (m of moves(); track m.node.id) {
          <button type="button" class="rail-move" [class.black]="m.node.ply % 2 === 0" [class.current]="m.node.id === currentId()"
                  [attr.data-mark]="m.mark" [attr.aria-current]="m.node.id === currentId() ? 'step' : null"
                  [title]="m.title" [attr.aria-label]="m.title" (click)="select.emit(m.node.id)"></button>
        }
      </nav>
    }
  `,
})
export class MoveRail {
  readonly game = input.required<MoveTreeView>();
  readonly marks = input<ReadonlyMap<number, Classification>>(new Map());
  readonly currentId = input<number | null>(null);
  readonly select = output<number | null>();

  protected readonly moves = computed(() => {
    const game = this.game();
    const byId = new Map(game.nodes.map((n) => [n.id, n]));
    const line: { node: MoveNode; mark: Classification | null; title: string }[] = [];
    let id: number | undefined = game.firstMoves[0];
    while (id !== undefined) {
      const node = byId.get(id);
      if (!node) break;
      const mark = this.marks().get(node.id) ?? null;
      line.push({ node, mark, title: moveLabel(node, true) + (mark ? ` · ${mark}` : '') });
      id = node.children[0];
    }
    return line;
  });
}
