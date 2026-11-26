import { Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, Game, MoveNode, eloLabel, problemMessage, resultLabel } from '../api/api';
import { Board } from './board';
import { GameDetailsCard } from './game-details';
import { Scoresheet, TreeAction } from './scoresheet';

/** The Game view: board, scoresheet and details. */
@Component({
  selector: 'app-game-review',
  imports: [Board, Scoresheet, GameDetailsCard],
  templateUrl: './game-review.html',
  host: { '(document:keydown)': 'onKey($event)' },
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
          this.currentId.set(this.lineEnd(null));
        },
        error: () => this.router.navigate(['/games']),
      });
    });
  }

  // ---------------------------------------------------------------- navigation

  protected onKey(event: KeyboardEvent) {
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, textarea, select, [contenteditable]')) return;
    if (event.ctrlKey || event.altKey || event.metaKey) return;

    const handled = (() => {
      switch (event.key) {
        case 'ArrowLeft':
          return this.goTo(this.current()?.parentId ?? null);
        case 'ArrowRight':
          return this.forward();
        case 'Home':
        case 'ArrowUp':
          return this.goTo(null);
        case 'End':
        case 'ArrowDown':
          return this.toEnd();
        case 'f':
        case 'F':
          this.flipped.update((f) => !f);
          return true;
        default:
          return false;
      }
    })();
    if (handled) event.preventDefault();
  }

  protected goTo(id: number | null): true {
    this.currentId.set(id);
    return true;
  }

  /** One move on along the current line (the first continuation). */
  protected forward(): true {
    return this.goTo(this.continuations(this.currentId())[0] ?? this.currentId());
  }

  /** To the last move of the current line. */
  protected toEnd(): true {
    return this.goTo(this.lineEnd(this.currentId()));
  }

  private continuations(id: number | null): number[] {
    const game = this.game();
    if (!game) return [];
    return id === null ? game.firstMoves : (this.nodes().get(id)?.children ?? []);
  }

  /** Follows first continuations from a node to the end of its line. */
  private lineEnd(id: number | null): number | null {
    let next = this.continuations(id)[0];
    while (next !== undefined) {
      id = next;
      next = this.continuations(id)[0];
    }
    return id;
  }

  // ---------------------------------------------------------------- editing

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

  protected onTreeAction({ action, node }: { action: TreeAction; node: MoveNode }) {
    const game = this.game();
    if (!game) return;
    const calls: Record<TreeAction, () => Observable<Game>> = {
      promote: () => this.api.promote(game.id, node.id),
      'delete-variation': () => this.api.deleteVariation(game.id, node.id),
      'delete-from-here': () => this.api.deleteFromHere(game.id, node.id),
    };
    this.error.set('');
    calls[action]().subscribe({
      next: (updated) => {
        const before = this.nodes();
        this.game.set(updated);
        // If the current move was deleted, step back to the last move that still exists.
        const current = this.currentId();
        if (current !== null && !this.nodes().has(current)) {
          let parent = node.parentId;
          while (parent !== null && !this.nodes().has(parent)) parent = before.get(parent)?.parentId ?? null;
          this.currentId.set(parent);
        }
      },
      error: (e) => this.error.set(problemMessage(e)),
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
}
