import { computed, signal } from '@angular/core';
import { Chess } from 'chess.js';
import { Observable } from 'rxjs';
import { MoveNode, MoveTreeView, problemMessage } from '../api/api';
import { INITIAL_FEN } from '../shared/chess';
import { continuations, lineEnd, pathTo } from './notation';

/** A move shown on the board while exploring: not part of the tree until the Owner adds it. */
export interface ExploreStep {
  uci: string;
  san: string;
  /** The position after the move. */
  fen: string;
}

/** What the Owner can do to a move from its context menu. */
export type TreeAction = 'promote' | 'delete-variation' | 'delete-from-here';

/** The calls that change a move tree on the server (a Game's or a Repertoire's). Each answers with the whole tree. */
export interface TreePort<T extends MoveTreeView> {
  /** Plays moves from the parent (null: the start); answers with the line's last move. */
  addLine(parentId: number | null, moves: string[]): Observable<{ nodeId: number; tree: T }>;
  promote(nodeId: number): Observable<T>;
  deleteVariation(nodeId: number): Observable<T>;
  deleteFrom(nodeId: number): Observable<T>;
}

export interface TreeSessionHooks {
  /** Before the board leaves the shown move or the tree changes, e.g. to save a comment being typed. */
  beforeChange?(): void;
  /** A move played on the board was refused: the view puts the board back. */
  rejectedMove?(): void;
}

/** Where a tree opens: the start, the end of its main line, or the main-line move at that ply. */
export type TreeStart = 'start' | 'end' | number;

/**
 * Working on a move tree in a view (the Game view, the Repertoire page): which move the board
 * shows, moving around it, exploring moves without saving them, and changing the tree through a
 * {@link TreePort}. Server answers replace the tree; when the shown move is gone, the board steps
 * back to the nearest move that is left. An answer for a tree that is no longer shown is dropped.
 */
export class TreeSession<T extends MoveTreeView> {
  private readonly treeSignal = signal<T | null>(null);
  private readonly currentIdSignal = signal<number | null>(null);
  private readonly exploreSignal = signal<ExploreStep[]>([]);
  private readonly errorSignal = signal('');
  private readonly startFen = signal(INITIAL_FEN);
  private port: TreePort<T> | null = null;
  /** Grows with every load, so answers for an earlier tree can be told apart. */
  private generation = 0;

  readonly tree = this.treeSignal.asReadonly();
  /** The move the board shows the position after; null is the starting position. Change it with goTo. */
  readonly currentId = this.currentIdSignal.asReadonly();
  /** Moves played on from the shown move without saving them. Empty when the board shows the tree. */
  readonly explore = this.exploreSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  readonly nodes = computed(() => new Map((this.tree()?.nodes ?? []).map((n) => [n.id, n])));
  readonly current = computed<MoveNode | null>(() => {
    const id = this.currentId();
    return id === null ? null : (this.nodes().get(id) ?? null);
  });
  /** The position of the shown move, where exploring starts from. */
  readonly treeFen = computed(() => this.current()?.fen ?? this.startFen());
  /** The position on the board: the shown move's, or the end of the exploration. */
  readonly fen = computed(() => this.explore().at(-1)?.fen ?? this.treeFen());
  readonly lastMove = computed(() => this.explore().at(-1)?.uci ?? this.current()?.uci ?? null);
  /** The moves (UCI) from the start to the board position, explored ones included. */
  readonly path = computed(() => [...pathTo(this.nodes(), this.currentId()), ...this.explore().map((s) => s.uci)]);
  /** The tree's main line (UCI). */
  readonly mainLine = computed(() => pathTo(this.nodes(), lineEnd(this.tree(), this.nodes(), null)));

  constructor(private readonly hooks: TreeSessionHooks = {}) {}

  /**
   * Shows a tree (null while it loads) and where the board opens. `startFen` is the position
   * before the first move. Answers still on their way for an earlier tree are dropped.
   */
  load(tree: T | null, port: TreePort<T> | null, at: TreeStart = 'end', startFen = INITIAL_FEN) {
    this.generation++;
    this.port = port;
    this.startFen.set(startFen);
    this.treeSignal.set(tree);
    this.exploreSignal.set([]);
    this.errorSignal.set('');
    this.currentIdSignal.set(this.startNode(at));
  }

  /** A newer version of the same tree (from a save elsewhere): the board stays, or steps back to the nearest move that is left. */
  replace(tree: T) {
    const before = this.nodes();
    this.treeSignal.set(tree);
    let id = this.currentId();
    while (id !== null && !this.nodes().has(id)) id = before.get(id)?.parentId ?? null;
    if (id !== this.currentId()) this.currentIdSignal.set(id);
  }

  // ---------------------------------------------------------------- moving around

  /** Shows a move (null: the start); any exploration is dropped. */
  goTo(id: number | null) {
    this.hooks.beforeChange?.();
    this.exploreSignal.set([]);
    this.currentIdSignal.set(id);
  }

  /** One move back: out of the exploration first, then to the parent. */
  back() {
    if (this.explore().length) this.exploreSignal.update((steps) => steps.slice(0, -1));
    else this.goTo(this.current()?.parentId ?? null);
  }

  /** One move on along the main continuation (not while exploring). */
  forward() {
    if (this.explore().length) return;
    const next = this.continuations(this.currentId())[0];
    if (next !== undefined) this.goTo(next);
  }

  toStart() {
    this.goTo(null);
  }

  /** To the last move of the shown line. */
  toEnd() {
    this.goTo(lineEnd(this.tree(), this.nodes(), this.currentId()));
  }

  /** The navigation keys (← → Home ↑ End ↓); true when the key was one of them. */
  handleKey(key: string): boolean {
    const actions: Record<string, () => void> = {
      ArrowLeft: () => this.back(),
      ArrowRight: () => this.forward(),
      Home: () => this.toStart(),
      ArrowUp: () => this.toStart(),
      End: () => this.toEnd(),
      ArrowDown: () => this.toEnd(),
    };
    const action = actions[key];
    action?.();
    return action !== undefined;
  }

  // ---------------------------------------------------------------- exploring

  /** A move from elsewhere (the Opening Tree, a Tablebase list): into the tree if it has it here, otherwise explored. */
  treeMove(step: ExploreStep) {
    if (!this.explore().length) {
      const recorded = this.continuations(this.currentId()).find((id) => this.nodes().get(id)?.uci === step.uci);
      if (recorded !== undefined) {
        this.goTo(recorded);
        return;
      }
    }
    this.exploreSignal.update((steps) => [...steps, step]);
  }

  /** Back to the tree's position, dropping the exploration. */
  stopExploring() {
    this.exploreSignal.set([]);
  }

  /** Saves the exploration into the tree from where it started, and shows its first move. */
  addExploration() {
    const steps = this.explore();
    if (steps.length) this.playLine([]);
  }

  // ---------------------------------------------------------------- editing

  /**
   * A move played on the board. While exploring it explores on (chess.js only gives instant
   * feedback; the backend checks every move once it is added, ADR 0002); otherwise it is added
   * to the tree and shown.
   */
  playMove(uci: string) {
    if (this.explore().length) {
      this.exploreOn(uci);
      return;
    }
    this.change(
      (port, from) => port.addLine(from, [uci]),
      ({ nodeId, tree }) => {
        this.treeSignal.set(tree);
        this.currentIdSignal.set(nodeId);
      },
      () => this.hooks.rejectedMove?.(),
    );
  }

  /**
   * Adds moves at the board position (explored moves first, then `moves`, e.g. an Engine line)
   * and shows the first of them, wherever the board went while the answer was on its way.
   */
  playLine(moves: string[]) {
    const all = [...this.explore().map((s) => s.uci), ...moves];
    if (!all.length) return;
    this.change(
      (port, from) => port.addLine(from, all),
      ({ tree }, from) => {
        this.treeSignal.set(tree);
        const first = this.continuations(from).find((id) => this.nodes().get(id)?.uci === all[0]);
        this.exploreSignal.set([]);
        this.currentIdSignal.set(first ?? from);
      },
    );
  }

  /** Promote, delete a Variation, or delete from a move on. */
  treeAction(action: TreeAction, node: MoveNode) {
    const calls: Record<TreeAction, (port: TreePort<T>) => Observable<T>> = {
      promote: (port) => port.promote(node.id),
      'delete-variation': (port) => port.deleteVariation(node.id),
      'delete-from-here': (port) => port.deleteFrom(node.id),
    };
    this.change(calls[action], (tree) => {
      this.exploreSignal.set([]); // explored moves may no longer start from a move of the tree
      this.replace(tree);
    });
  }

  /**
   * Sends a change through the port, from the move shown now, and applies its answer (with that
   * move), unless another tree is shown by then. Without a tree to change, nothing is sent.
   */
  private change<R>(
    call: (port: TreePort<T>, from: number | null) => Observable<R>,
    applied: (answer: R, from: number | null) => void,
    failed?: () => void,
  ) {
    const port = this.port;
    if (!port) {
      failed?.();
      return;
    }
    this.hooks.beforeChange?.();
    this.errorSignal.set('');
    const generation = this.generation;
    const from = this.currentId();
    call(port, from).subscribe({
      next: (answer) => {
        if (generation === this.generation) applied(answer, from);
      },
      error: (e) => {
        if (generation !== this.generation) return;
        this.errorSignal.set(problemMessage(e));
        failed?.();
      },
    });
  }

  private exploreOn(uci: string) {
    const chess = new Chess(this.fen());
    try {
      const move = chess.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] });
      this.exploreSignal.update((steps) => [...steps, { uci, san: move.san, fen: chess.fen() }]);
    } catch {
      this.hooks.rejectedMove?.();
    }
  }

  private continuations(id: number | null): number[] {
    return continuations(this.tree(), this.nodes(), id);
  }

  private startNode(at: TreeStart): number | null {
    if (at === 'start') return null;
    if (at === 'end') return lineEnd(this.tree(), this.nodes(), null);
    let id: number | null = null;
    for (let i = 0; i < at; i++) {
      const next: number | undefined = this.continuations(id)[0];
      if (next === undefined) break;
      id = next;
    }
    return id;
  }
}
