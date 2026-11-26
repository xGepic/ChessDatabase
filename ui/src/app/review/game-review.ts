import { Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { Api, Game, MoveNode, eloLabel, problemMessage, resultLabel } from '../api/api';
import { Board } from './board';
import { GameDetailsCard } from './game-details';
import { Scoresheet } from './scoresheet';

/** The Game view: board, scoresheet and details. */
@Component({
  selector: 'app-game-review',
  imports: [Board, Scoresheet, GameDetailsCard],
  templateUrl: './game-review.html',
})
export class GameReview {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** Route parameter. */
  readonly id = input.required<string>();

  protected readonly game = signal<Game | null>(null);
  /** The move the board shows the position after; null is the starting position. */
  protected readonly currentId = signal<number | null>(null);
  protected readonly error = signal('');
  protected readonly flipped = signal(false);
  protected readonly eloLabel = eloLabel;
  protected readonly resultLabel = resultLabel;

  private readonly board = viewChild(Board);

  protected readonly nodes = computed(() => new Map((this.game()?.nodes ?? []).map((n) => [n.id, n])));
  protected readonly current = computed<MoveNode | null>(() => {
    const id = this.currentId();
    return id === null ? null : (this.nodes().get(id) ?? null);
  });
  protected readonly fen = computed(() => this.current()?.fen ?? this.game()?.startFen ?? '');
  protected readonly orientation = computed<'white' | 'black'>(() => {
    const owner = this.game()?.ownerColor ?? 'white';
    return this.flipped() ? (owner === 'white' ? 'black' : 'white') : owner;
  });

  constructor() {
    effect(() => {
      const id = Number(this.id());
      this.api.getGame(id).subscribe({
        next: (game) => {
          this.game.set(game);
          this.currentId.set(this.mainLineEnd(game));
        },
        error: () => this.router.navigate(['/games']),
      });
    });
  }

  protected onBoardMove(uci: string) {
    const game = this.game();
    if (!game) return;
    this.error.set('');
    this.api.addMove(game.id, this.currentId(), uci).subscribe({
      next: ({ nodeId, game: updated }) => {
        this.game.set(updated);
        this.currentId.set(nodeId);
      },
      error: (e) => {
        this.error.set(problemMessage(e));
        this.board()?.reset();
      },
    });
  }

  protected onDetailsSaved(game: Game) {
    this.game.set(game);
  }

  protected onDelete() {
    const game = this.game();
    if (!game) return;
    this.api.deleteGame(game.id).subscribe(() => this.router.navigate(['/games']));
  }

  private mainLineEnd(game: Game): number | null {
    const byId = new Map(game.nodes.map((n) => [n.id, n]));
    let id: number | null = game.firstMoves[0] ?? null;
    while (id !== null) {
      const next: number | undefined = byId.get(id)?.children[0];
      if (next === undefined) break;
      id = next;
    }
    return id;
  }
}
