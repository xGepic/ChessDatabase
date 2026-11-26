import { Component, DestroyRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, MoveNode, OwnerColour, Repertoire, problemMessage } from '../api/api';
import { Board } from '../review/board';
import { continuations, lineEnd } from '../review/notation';
import { Scoresheet, TreeAction } from '../review/scoresheet';
import { INITIAL_FEN } from '../shared/chess';

/**
 * The Repertoire editor: one move tree for White and one for Black, edited like a Game. Playing a
 * move on the board adds it (a Variation if a move is already there); right-click a move to
 * promote or delete; every move can carry a comment. Everything saves at once.
 */
@Component({
  selector: 'app-repertoire-page',
  imports: [Board, Scoresheet, RouterLink],
  host: { '(document:keydown)': 'onKey($event)' },
  template: `
    <div class="review repertoire">
      <div class="board-column">
        <app-board [fen]="fen()" [orientation]="side()" [lastMove]="current()?.uci ?? null" (move)="onBoardMove($event)" />
        <div class="board-tools">
          <button type="button" (click)="goTo(null)" title="Start (Home)" aria-label="Start">⏮</button>
          <button type="button" (click)="goTo(current()?.parentId ?? null)" title="Back (←)" aria-label="Back">◀</button>
          <button type="button" (click)="goTo(next(currentId()) ?? currentId())" title="Forward (→)" aria-label="Forward">▶</button>
          <button type="button" (click)="goTo(end(currentId()))" title="End (End)" aria-label="End">⏭</button>
          @if (error()) { <span class="board-error" role="alert" data-testid="repertoire-error">{{ error() }}</span> }
        </div>
      </div>

      <section class="card scoresheet-card">
        <header class="sheet-head">
          <div class="segmented" role="tablist" aria-label="Repertoire">
            <a role="tab" class="button" routerLink="/repertoire/white" [class.on]="side() === 'white'" [attr.aria-selected]="side() === 'white'"
               data-testid="repertoire-white">White</a>
            <a role="tab" class="button" routerLink="/repertoire/black" [class.on]="side() === 'black'" [attr.aria-selected]="side() === 'black'"
               data-testid="repertoire-black">Black</a>
          </div>
          <p class="hint">What you play with {{ side() === 'white' ? 'White' : 'Black' }}. Play moves on the board to add them;
            right-click a move to promote or delete it.</p>
        </header>
        @if (repertoire(); as r) {
          <app-scoresheet [game]="r" [currentId]="currentId()" (select)="goTo($event)" (action)="onTreeAction($event)" />
        } @else {
          <p class="empty">Loading…</p>
        }
      </section>

      <div class="side-column">
        <section class="card" aria-label="Comment">
          <header class="card-head"><h2>Comment</h2><span class="spacer"></span>
            @if (current(); as c) { <span class="hint mono">{{ c.san }}</span> }</header>
          @if (current(); as c) {
            <div class="card-body">
              <textarea class="comment-input" data-testid="repertoire-comment" rows="4" placeholder="Why this move, what to remember…"
                        [value]="draft()" (input)="onComment($any($event.target).value)" (blur)="saveComment()"></textarea>
              @if (commentStatus()) { <p class="hint"><span class="status" data-testid="repertoire-comment-status">{{ commentStatus() }}</span></p> }
            </div>
          } @else {
            <p class="assessment-note">Select a move to comment on it.</p>
          }
        </section>
      </div>
    </div>
  `,
})
export class RepertoirePage {
  private readonly api = inject(Api);
  private readonly board = viewChild(Board);

  /** Route parameter: "white" or "black". */
  readonly sideParam = input.required<string>({ alias: 'side' });
  protected readonly side = computed<OwnerColour>(() => (this.sideParam() === 'black' ? 'black' : 'white'));

  protected readonly repertoire = signal<Repertoire | null>(null);
  protected readonly currentId = signal<number | null>(null);
  protected readonly error = signal('');
  protected readonly commentStatus = signal('');
  /**
   * The comment box's text. It changes only when another move is shown, never from a save, so
   * typing is never interrupted; a comment still waiting to be saved is saved before moving on.
   */
  protected readonly draft = signal('');
  private pendingComment: { side: OwnerColour; nodeId: number; text: string } | null = null;
  private commentTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly nodes = computed(() => new Map((this.repertoire()?.nodes ?? []).map((n) => [n.id, n])));
  protected readonly current = computed<MoveNode | null>(() => {
    const id = this.currentId();
    return id === null ? null : (this.nodes().get(id) ?? null);
  });
  protected readonly fen = computed(() => this.current()?.fen ?? INITIAL_FEN);

  constructor() {
    effect((onCleanup) => {
      const side = this.side();
      untracked(() => {
        this.saveComment();
        this.repertoire.set(null);
        this.show(null);
      });
      // A slow answer for the side shown before must not replace this one.
      const request = this.api.repertoire(side).subscribe((r) => this.repertoire.set(r));
      onCleanup(() => request.unsubscribe());
    });
    inject(DestroyRef).onDestroy(() => this.saveComment());
  }

  /** Shows a move (null: the start) with its comment. */
  private show(id: number | null) {
    this.commentStatus.set('');
    this.currentId.set(id);
    this.draft.set(id === null ? '' : (this.nodes().get(id)?.commentAfter ?? ''));
  }

  protected next(id: number | null): number | undefined {
    return continuations(this.repertoire(), this.nodes(), id)[0];
  }

  protected end(id: number | null): number | null {
    return lineEnd(this.repertoire(), this.nodes(), id);
  }

  protected goTo(id: number | null) {
    this.saveComment();
    this.show(id);
  }

  protected onKey(event: KeyboardEvent) {
    if ((event.target as HTMLElement | null)?.closest('input, textarea, select, [contenteditable]')) return;
    const moves: Record<string, () => void> = {
      ArrowLeft: () => this.goTo(this.current()?.parentId ?? null),
      ArrowRight: () => this.goTo(this.next(this.currentId()) ?? this.currentId()),
      Home: () => this.goTo(null),
      ArrowUp: () => this.goTo(null),
      End: () => this.goTo(this.end(this.currentId())),
      ArrowDown: () => this.goTo(this.end(this.currentId())),
    };
    const move = moves[event.key];
    if (!move) return;
    move();
    event.preventDefault();
  }

  /** A move played on the board is added to the Repertoire there (followed if it is already in it). */
  protected onBoardMove(uci: string) {
    this.error.set('');
    this.saveComment();
    const side = this.side();
    this.api.addRepertoireLine(side, this.currentId(), [uci]).subscribe({
      next: ({ nodeId, repertoire }) => {
        if (this.side() !== side) return;
        this.repertoire.set(repertoire);
        this.show(nodeId);
      },
      error: (e) => {
        if (this.side() !== side) return;
        this.error.set(problemMessage(e));
        this.board()?.reset();
      },
    });
  }

  protected onTreeAction({ action, node }: { action: TreeAction; node: MoveNode }) {
    const side = this.side();
    const call = {
      promote: () => this.api.promoteRepertoire(side, node.id),
      'delete-variation': () => this.api.deleteRepertoireVariation(side, node.id),
      'delete-from-here': () => this.api.deleteRepertoireFrom(side, node.id),
    }[action];
    this.error.set('');
    this.saveComment();
    call().subscribe({
      next: (repertoire) => {
        if (this.side() !== side) return;
        const before = this.nodes();
        this.repertoire.set(repertoire);
        // If the move on the board was deleted, step back to the last one that still exists.
        let id = this.currentId();
        while (id !== null && !this.nodes().has(id)) id = before.get(id)?.parentId ?? null;
        if (id !== this.currentId()) this.show(id);
      },
      error: (e) => {
        if (this.side() === side) this.error.set(problemMessage(e));
      },
    });
  }

  /** Comments save shortly after typing stops, when the box loses focus, or before anything else happens. */
  protected onComment(text: string) {
    const nodeId = this.currentId();
    if (nodeId === null) return;
    this.draft.set(text);
    this.pendingComment = { side: this.side(), nodeId, text };
    this.commentStatus.set('');
    clearTimeout(this.commentTimer);
    this.commentTimer = setTimeout(() => this.saveComment(), 500);
  }

  /** Saves the comment still waiting, if any. */
  protected saveComment() {
    clearTimeout(this.commentTimer);
    const pending = this.pendingComment;
    if (!pending) return;
    this.pendingComment = null;
    this.api.setRepertoireComment(pending.side, pending.nodeId, pending.text).subscribe({
      next: (saved) => {
        if (this.side() !== pending.side) return;
        // Only this comment is taken from the answer: other changes may have been answered since.
        const comment = saved.nodes.find((n) => n.id === pending.nodeId)?.commentAfter ?? null;
        this.repertoire.update((r) => r && { ...r, nodes: r.nodes.map((n) => (n.id === pending.nodeId ? { ...n, commentAfter: comment } : n)) });
        if (this.currentId() === pending.nodeId) this.commentStatus.set('Saved');
      },
      error: (e) => {
        if (this.side() === pending.side && this.currentId() === pending.nodeId) this.commentStatus.set(problemMessage(e));
      },
    });
  }
}
