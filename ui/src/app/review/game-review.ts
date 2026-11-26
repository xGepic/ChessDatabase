import { Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AnalysisLine, Api, Classification, EngineAssessment, Game, MoveNode, PositionGame, eloLabel, problemMessage, resultLabel,
} from '../api/api';
import { AnalysisQueueService } from '../engine/analysis-queue';
import { AssessmentCard } from '../engine/assessment-card';
import { OpeningTreePanel } from '../research/opening-tree';
import { EnginePanel } from '../engine/engine-panel';
import { EngineSession } from '../engine/engine-session';
import { EvalBar } from '../engine/eval-bar';
import { Board } from './board';
import { AnnotationCard } from './annotation-card';
import { GameDetailsCard } from './game-details';
import { Scoresheet, TreeAction } from './scoresheet';

/** The Game view: board, scoresheet and details. */
@Component({
  selector: 'app-game-review',
  imports: [Board, Scoresheet, AnnotationCard, GameDetailsCard, EnginePanel, EvalBar, AssessmentCard, OpeningTreePanel, RouterLink],
  providers: [EngineSession],
  templateUrl: './game-review.html',
  host: { '(document:keydown)': 'onKey($event)' },
})
export class GameReview {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  protected readonly engine = inject(EngineSession);
  protected readonly queue = inject(AnalysisQueueService);

  /** Route parameter. */
  readonly id = input.required<string>();
  /** Query parameter: open the Game at this main-line ply (0 = the start) instead of its end. */
  readonly ply = input<string | undefined>();

  protected readonly game = signal<Game | null>(null);
  /** The move the board shows the position after; null is the starting position. */
  protected readonly currentId = signal<number | null>(null);
  protected readonly error = signal('');
  protected readonly flipped = signal(false);
  protected readonly eloLabel = eloLabel;
  protected readonly resultLabel = resultLabel;

  private readonly board = viewChild(Board);

  /** The Game's Engine Assessment, if it has one. */
  protected readonly assessment = signal<EngineAssessment | null>(null);
  protected readonly analysisError = signal('');
  protected readonly copyMessage = signal('');
  /** Move Classifications by move, for the scoresheet. */
  protected readonly marks = computed(
    () =>
      new Map(
        (this.assessment()?.moves ?? [])
          .filter((m) => m.nodeId !== null && m.classification)
          .map((m) => [m.nodeId!, m.classification as Classification]),
      ),
  );

  protected readonly nodes = computed(() => new Map((this.game()?.nodes ?? []).map((n) => [n.id, n])));
  protected readonly current = computed<MoveNode | null>(() => {
    const id = this.currentId();
    return id === null ? null : (this.nodes().get(id) ?? null);
  });
  /** Event · Round · Date, as far as known. */
  protected readonly meta = computed(() => {
    const g = this.game();
    return g ? [g.event?.name, g.round ? `Round ${g.round}` : null, g.date].filter((x) => !!x).join(" · ") : "";
  });
  protected readonly fen = computed(() => this.current()?.fen ?? this.game()?.startFen ?? '');
  protected readonly orientation = computed<'white' | 'black'>(() => {
    const owner = this.game()?.ownerColour ?? 'white';
    return this.flipped() ? (owner === 'white' ? 'black' : 'white') : owner;
  });

  /** The moves from the start to the board position, in UCI: what the Engine is given. */
  private readonly path = computed(() => {
    const moves: string[] = [];
    for (let node = this.current(); node; node = node.parentId === null ? null : (this.nodes().get(node.parentId) ?? null))
      moves.unshift(node.uci);
    return moves;
  });

  constructor() {
    // The Engine follows the board: every new position restarts the analysis (briefly debounced for held keys).
    let followTimer: ReturnType<typeof setTimeout> | undefined;
    effect(() => {
      const moves = this.path();
      if (!this.engine.on()) return;
      clearTimeout(followTimer);
      followTimer = setTimeout(() => this.engine.analyse(moves), 80);
    });

    this.queue.watch();
    this.queue.finished.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event.gameId !== this.game()?.id) return;
      this.analysisError.set(event.error ?? '');
      if (!event.error) this.loadAssessment(event.gameId);
    });

    effect(() => {
      const id = Number(this.id());
      this.ply(); // opening the same Game at another position reloads it there
      this.api.getGame(id).subscribe({
        next: (game) => {
          this.game.set(game);
          this.currentId.set(this.startNode());
          this.loadAssessment(game.id);
        },
        error: () => this.router.navigate(['/games']),
      });
    });
  }

  // ---------------------------------------------------------------- Game Analysis

  private loadAssessment(gameId: number) {
    this.api.getAssessment(gameId).subscribe({
      next: (a) => this.assessment.set(a),
      error: (e: HttpErrorResponse) => {
        if (e.status === 404) this.assessment.set(null);
      },
    });
  }

  protected onAnalyse(secondsPerMove: number | undefined) {
    const game = this.game();
    if (!game) return;
    this.analysisError.set('');
    this.api.analyseGame(game.id, secondsPerMove).subscribe({
      next: (status) => this.queue.set(status),
      error: (e) => this.analysisError.set(problemMessage(e)),
    });
  }

  /** Copies one classified move into the Game (ADR 0003: only ever on the Owner's request). */
  protected onCopy(ply: number) {
    const game = this.game();
    if (!game) return;
    this.api.copyAssessedMove(game.id, ply).subscribe({
      next: ({ copied, game: updated }) => {
        this.game.set(updated);
        this.copyMessage.set(copied ? 'Copied into the Game.' : 'Already in the Game.');
      },
      error: (e) => this.copyMessage.set(problemMessage(e)),
    });
  }

  protected onCopyAll() {
    const game = this.game();
    if (!game) return;
    this.api.copyMistakesAndBlunders(game.id).subscribe({
      next: ({ copied, game: updated }) => {
        this.game.set(updated);
        this.copyMessage.set(
          copied === 0 ? 'Already in the Game.' : copied === 1 ? 'Copied 1 move into the Game.' : `Copied ${copied} moves into the Game.`,
        );
      },
      error: (e) => this.copyMessage.set(problemMessage(e)),
    });
  }

  /** From the evaluation graph or the list of classified moves. */
  protected onGoToPly(ply: number) {
    if (ply === 0) {
      this.goTo(null);
      return;
    }
    const nodeId = this.assessment()?.moves[ply - 1]?.nodeId;
    if (nodeId !== null && nodeId !== undefined) this.goTo(nodeId);
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
        case 'e':
        case 'E':
          this.toggleEngine();
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

  /** Where a Game opens: the main-line move at the requested ply, otherwise the end of the main line. */
  private startNode(): number | null {
    const ply = Number(this.ply());
    if (this.ply() === undefined || !Number.isInteger(ply) || ply < 0) return this.lineEnd(null);
    let id: number | null = null;
    for (let i = 0; i < ply; i++) {
      const next: number | undefined = this.continuations(id)[0];
      if (next === undefined) break;
      id = next;
    }
    return id;
  }

  /** Opens a Game from the Opening Tree at the position it shares with the board. */
  protected onOpenAtPosition(entry: PositionGame) {
    this.router.navigate(['/games', entry.game.id], { queryParams: { ply: entry.ply } });
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

  // ---------------------------------------------------------------- engine

  protected toggleEngine() {
    if (this.engine.on()) this.engine.stop();
    else this.engine.start(this.path());
  }

  /** Adds the Engine line at the board position (as a Variation if a move is recorded there) and steps into it. */
  protected onPlayLine(line: AnalysisLine) {
    const game = this.game();
    if (!game) return;
    const from = this.currentId();
    this.error.set('');
    this.api.addLine(game.id, from, line.uci).subscribe({
      next: ({ game: updated }) => {
        this.game.set(updated);
        const first = this.continuations(from).find((id) => this.nodes().get(id)?.uci === line.uci[0]);
        if (first !== undefined) this.currentId.set(first);
      },
      error: (e) => this.error.set(problemMessage(e)),
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
