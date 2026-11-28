import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription, map } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AnalysisLine, Api, BoardMove, Classification, Deviation, EngineAssessment, Game, MoveNode, PositionGame, eloLabel, problemMessage, resultLabel,
} from '../api/api';
import { nextPly, numberedChoices, numberedLine } from '../engine/scores';
import { openGameAt } from '../shared/chess';
import { AnalysisQueueService } from '../engine/analysis-queue';
import { AssessmentCard } from '../engine/assessment-card';
import { OpeningTreePanel } from '../research/opening-tree';
import { TablebasePanel } from '../research/tablebase-panel';
import { EnginePanel } from '../engine/engine-panel';
import { EngineSession } from '../engine/engine-session';
import { EvalBar } from '../engine/eval-bar';
import { Board } from './board';
import { ExportTools } from './export-tools';
import { AnnotationCard } from './annotation-card';
import { GameDetailsCard } from './game-details';
import { Scoresheet } from './scoresheet';
import { MaterialBalance } from './material-balance';
import { Icon } from '../shared/icon';
import { TreeAction, TreePort, TreeSession } from './tree-session';

type SideTab = 'analysis' | 'tree' | 'details';

/** The Game view: board, scoresheet and details. */
@Component({
  selector: 'app-game-review',
  imports: [Board, Scoresheet, AnnotationCard, GameDetailsCard, EnginePanel, EvalBar, AssessmentCard, OpeningTreePanel, TablebasePanel, ExportTools, MaterialBalance, Icon, RouterLink],
  providers: [EngineSession],
  templateUrl: './game-review.html',
  host: { '(document:keydown)': 'onKey($event)', '(document:click)': 'closeExportMenu($event)' },
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

  /** The Game's moves on the board: the shown move, navigation, exploring and edits. */
  protected readonly session = new TreeSession<Game>({ rejectedMove: () => this.board()?.reset() });
  protected readonly game = this.session.tree;
  /** The move the board shows the position after; null is the starting position. */
  protected readonly currentId = this.session.currentId;
  protected readonly error = this.session.error;
  protected readonly flipped = signal(false);
  /** The tools beside the scoresheet, one at a time. */
  protected readonly sideTabs: { id: SideTab; label: string }[] = [
    { id: 'analysis', label: 'Game Analysis' },
    { id: 'tree', label: 'Opening Tree' },
    { id: 'details', label: 'Details' },
  ];
  protected readonly sideTab = signal<SideTab>('analysis');
  protected readonly eloLabel = eloLabel;
  protected readonly resultLabel = resultLabel;

  private readonly board = viewChild(Board);
  private readonly exportMenu = viewChild<ElementRef<HTMLDetailsElement>>('exportMenu');

  /** The Export menu closes on a click outside it, and on Escape. */
  protected closeExportMenu(event: Event) {
    const menu = this.exportMenu()?.nativeElement;
    if (menu?.open && !menu.contains(event.target as Node)) menu.open = false;
  }

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

  protected readonly nodes = this.session.nodes;
  protected readonly current = this.session.current;
  /** Event · Round · Date, as far as known. */
  protected readonly meta = computed(() => {
    const g = this.game();
    return g ? [g.event?.name, g.round ? `Round ${g.round}` : null, g.date].filter((x) => !!x).join(" · ") : "";
  });
  /** Moves shown on from the Game's position without saving them (Opening Tree, board). */
  protected readonly explore = this.session.explore;
  /** The position on the board: the Game's, or the end of the exploration. */
  protected readonly fen = this.session.fen;
  protected readonly lastMove = this.session.lastMove;
  /** The exploration as move text, e.g. "3...Bc5 4.c3". */
  protected readonly exploreLine = computed(() =>
    numberedLine(nextPly(this.session.treeFen()), this.explore().map((s) => s.san)),
  );
  protected readonly orientation = computed<'white' | 'black'>(() => {
    const owner = this.game()?.ownerColour ?? 'white';
    return this.flipped() ? (owner === 'white' ? 'black' : 'white') : owner;
  });

  /** The Engine's best move, drawn on the board while the Engine is on. */
  protected readonly engineArrow = computed(() => (this.engine.on() ? (this.engine.update()?.lines?.[0]?.uci?.[0] ?? null) : null));

  /** The moves from the start to the board position, in UCI: what the Engine is given. */
  private readonly path = this.session.path;

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
          this.session.load(game, this.portFor(game.id), this.startAt(), game.startFen);
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
        this.session.replace(updated);
        this.copyMessage.set(copied ? 'Copied into the Game.' : 'Already in the Game.');
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
    if (event.key === 'Escape') {
      const menu = this.exportMenu()?.nativeElement;
      if (menu) menu.open = false;
    }
    if (event.ctrlKey || event.altKey || event.metaKey) return;

    const handled = (() => {
      switch (event.key) {
        case 'f':
        case 'F':
          this.flipped.update((f) => !f);
          return true;
        case 'e':
        case 'E':
          this.toggleEngine();
          return true;
        default:
          return this.session.handleKey(event.key);
      }
    })();
    if (handled) event.preventDefault();
  }

  /** Shows a move of the Game; any exploration is dropped. */
  protected goTo(id: number | null) {
    this.session.goTo(id);
  }

  protected forward() {
    this.session.forward();
  }

  protected toEnd() {
    this.session.toEnd();
  }

  /** Where a Game opens: the main-line move at the requested ply, otherwise the end of the main line. */
  private startAt(): number | 'end' {
    const ply = Number(this.ply());
    return this.ply() === undefined || !Number.isInteger(ply) || ply < 0 ? 'end' : ply;
  }

  /** How the session changes this Game's moves on the server. */
  private portFor(gameId: number): TreePort<Game> {
    return {
      addLine: (parentId, moves) => this.api.addLine(gameId, parentId, moves).pipe(map(({ nodeId, game }) => ({ nodeId, tree: game }))),
      promote: (nodeId) => this.api.promote(gameId, nodeId),
      deleteVariation: (nodeId) => this.api.deleteVariation(gameId, nodeId),
      deleteFrom: (nodeId) => this.api.deleteFromHere(gameId, nodeId),
    };
  }

  /** Opens a Game from the Opening Tree at the position it shares with the board. */
  protected onOpenAtPosition(entry: PositionGame) {
    openGameAt(this.router, entry.game.id, entry.ply);
  }

  // ---------------------------------------------------------------- exploring

  /**
   * A move clicked in the Opening Tree: if the Game has it here, step into it; otherwise show
   * it on the board without saving (exploring).
   */
  protected onTreeMove(move: BoardMove) {
    this.session.treeMove({ uci: move.uci, san: move.san, fen: move.fen });
  }

  /** Back to the Game's position, dropping the exploration. */
  protected stopExploring() {
    this.session.stopExploring();
  }

  /** Saves the exploration into the Game from where it started (a Variation, or the main line's end). */
  protected addExploration() {
    this.session.addExploration();
  }

  // ---------------------------------------------------------------- Repertoire

  // ---------------------------------------------------------------- Deviation

  protected readonly deviation = signal<Deviation | null>(null);
  /** The main line as text: the Deviation is looked up again whenever it changes. */
  private readonly mainLineKey = computed(() => {
    const game = this.game();
    if (!game) return null;
    return `${game.id}:${game.ownerColour}:${this.session.mainLine().join(' ')}`;
  });
  private readonly loadDeviationOnChange = effect(() => {
    if (this.mainLineKey() !== null) untracked(() => this.loadDeviation());
  });

  private deviationRequest: Subscription | undefined;
  /** The Game the shown Deviation belongs to. */
  private deviationGameId: number | null = null;

  /** Asks again; an answer still on its way (for another Game or an older main line) is dropped. */
  private loadDeviation() {
    const game = this.game();
    this.deviationRequest?.unsubscribe();
    if (this.deviation() && this.deviationGameId !== game?.id) this.deviation.set(null);
    if (!game) return;
    this.deviationRequest = this.api.deviation(game.id).subscribe({
      next: (d) => {
        this.deviationGameId = game.id;
        this.deviation.set(d);
      },
      error: () => this.deviation.set(null),
    });
  }

  /** "You left your Repertoire with 3.Bc4 (your Repertoire: 3.Nf3)" and the like. */
  protected readonly deviationText = computed(() => {
    const d = this.deviation();
    const game = this.game();
    if (!d || !game) return null;
    const colour = game.ownerColour === 'white' ? 'White' : 'Black';
    if (d.status === 'gameEnded') return `In your ${colour} Repertoire to the end.`;
    const played = numberedLine(d.ply!, [d.san!]);
    if (d.status === 'endOfRepertoire')
      return d.ply === 1 ? `Your ${colour} Repertoire has no moves yet.` : `End of your ${colour} Repertoire at ${played}.`;
    const expected = numberedChoices(d.ply!, d.expected);
    const who = d.by === 'owner' ? 'You' : 'Your Opponent';
    return `${who} left your Repertoire with ${played} (your Repertoire: ${expected}).`;
  });

  protected readonly repertoireMessage = signal('');
  /** The note is about the position it was added at; it goes when the board shows another. */
  private readonly clearRepertoireMessage = effect(() => {
    this.path();
    untracked(() => this.repertoireMessage.set(''));
  });

  /** Adds the line up to the board position (explored moves included) to the Repertoire of the colour the Owner had. */
  protected addToRepertoire() {
    const game = this.game();
    const moves = this.path();
    if (!game || !moves.length) return;
    const side = game.ownerColour;
    this.api.addRepertoireLine(side, null, moves).subscribe({
      next: () => {
        this.repertoireMessage.set(`Added to your ${side === 'white' ? 'White' : 'Black'} Repertoire.`);
        this.loadDeviation();
      },
      error: (e) => this.repertoireMessage.set(problemMessage(e)),
    });
  }

  // ---------------------------------------------------------------- editing

  /** A board move: added to the Game, or explored on while exploring. */
  protected onBoardMove(uci: string) {
    this.session.playMove(uci);
  }

  // ---------------------------------------------------------------- engine

  protected toggleEngine() {
    if (this.engine.on()) this.engine.stop();
    else this.engine.start(this.path());
  }

  /**
   * Adds the Engine line at the board position (as a Variation if a move is recorded there) and
   * steps into it. While exploring, the explored moves are added first.
   */
  protected onPlayLine(line: AnalysisLine) {
    this.session.playLine(line.uci);
  }

  protected onTreeAction({ action, node }: { action: TreeAction; node: MoveNode }) {
    this.session.treeAction(action, node);
  }

  protected onDetailsSaved(game: Game) {
    this.session.replace(game);
  }

  protected onDelete() {
    const game = this.game();
    if (!game) return;
    this.api.deleteGame(game.id).subscribe(() => this.router.navigate(['/games']));
  }
}
