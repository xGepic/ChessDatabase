import { Component, DestroyRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Api, MoveNode, OwnerColour, Repertoire, problemMessage } from '../api/api';
import { Board } from '../review/board';
import { Icon } from '../shared/icon';
import { Scoresheet } from '../review/scoresheet';
import { TreeAction, TreePort, TreeSession } from '../review/tree-session';

/**
 * The Repertoire editor: one move tree for White and one for Black, edited like a Game. Playing a
 * move on the board adds it (a Variation if a move is already there); right-click a move to
 * promote or delete; every move can carry a comment. Everything saves at once.
 */
@Component({
  selector: 'app-repertoire-page',
  imports: [Board, Scoresheet, RouterLink, Icon],
  host: { '(document:keydown)': 'onKey($event)' },
  template: `
    <div class="review repertoire">
      <div class="board-column">
        <app-board [fen]="session.fen()" [orientation]="side()" [lastMove]="session.lastMove()" (move)="session.playMove($event)" />
        <div class="board-tools">
          <button type="button" class="icon-button" (click)="goTo(null)" title="Start (Home)" aria-label="Start"><app-icon name="first" /></button>
          <button type="button" class="icon-button" (click)="session.back()" title="Back (←)" aria-label="Back"><app-icon name="back" /></button>
          <button type="button" class="icon-button" (click)="session.forward()" title="Forward (→)" aria-label="Forward"><app-icon name="forward" /></button>
          <button type="button" class="icon-button" (click)="session.toEnd()" title="End (End)" aria-label="End"><app-icon name="last" /></button>
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
          <a routerLink="/repertoire/deviations" data-testid="my-deviations" title="Games where you left your own Repertoire">My Deviations</a>
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

  /** The Repertoire on the board: the shown move, navigation and edits. A comment being typed is saved first. */
  protected readonly session = new TreeSession<Repertoire>({
    beforeChange: () => this.saveComment(),
    rejectedMove: () => this.board()?.reset(),
  });
  protected readonly repertoire = this.session.tree;
  protected readonly currentId = this.session.currentId;
  protected readonly error = this.session.error;
  protected readonly commentStatus = signal('');
  /**
   * The comment box's text. It changes only when another move is shown, never from a save, so
   * typing is never interrupted; a comment still waiting to be saved is saved before moving on.
   */
  protected readonly draft = signal('');
  private pendingComment: { side: OwnerColour; nodeId: number; text: string } | null = null;
  private commentTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly nodes = this.session.nodes;
  protected readonly current = this.session.current;

  constructor() {
    effect((onCleanup) => {
      const side = this.side();
      untracked(() => {
        this.saveComment();
        this.session.load(null, null, 'start');
      });
      // A slow answer for the side shown before must not replace this one.
      const request = this.api.repertoire(side).subscribe((r) => this.session.load(r, this.portFor(side), 'start'));
      onCleanup(() => request.unsubscribe());
    });
    // The comment box shows the comment of the move on the board.
    effect(() => {
      const id = this.currentId();
      untracked(() => {
        this.commentStatus.set('');
        this.draft.set(id === null ? '' : (this.nodes().get(id)?.commentAfter ?? ''));
      });
    });
    inject(DestroyRef).onDestroy(() => this.saveComment());
  }

  /** How the session changes one side's Repertoire on the server. */
  private portFor(side: OwnerColour): TreePort<Repertoire> {
    return {
      addLine: (parentId, moves) =>
        this.api.addRepertoireLine(side, parentId, moves).pipe(map(({ nodeId, repertoire }) => ({ nodeId, tree: repertoire }))),
      promote: (nodeId) => this.api.promoteRepertoire(side, nodeId),
      deleteVariation: (nodeId) => this.api.deleteRepertoireVariation(side, nodeId),
      deleteFrom: (nodeId) => this.api.deleteRepertoireFrom(side, nodeId),
    };
  }

  protected goTo(id: number | null) {
    this.session.goTo(id);
  }

  protected onKey(event: KeyboardEvent) {
    if ((event.target as HTMLElement | null)?.closest('input, textarea, select, [contenteditable]')) return;
    if (this.session.handleKey(event.key)) event.preventDefault();
  }

  protected onTreeAction({ action, node }: { action: TreeAction; node: MoveNode }) {
    this.session.treeAction(action, node);
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
        const shown = this.repertoire();
        if (shown) this.session.replace({ ...shown, nodes: shown.nodes.map((n) => (n.id === pending.nodeId ? { ...n, commentAfter: comment } : n)) });
        if (this.currentId() === pending.nodeId) this.commentStatus.set('Saved');
      },
      error: (e) => {
        if (this.side() === pending.side && this.currentId() === pending.nodeId) this.commentStatus.set(problemMessage(e));
      },
    });
  }
}
