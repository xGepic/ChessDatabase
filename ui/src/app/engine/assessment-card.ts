import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { AnalysisQueue, Api, AssessedMove, EngineAssessment, OwnerColour } from '../api/api';
import { EvalGraph } from './eval-graph';

/**
 * The Engine Assessment of the Game (ADR 0003: separate from the Owner's Annotations): Accuracy,
 * the evaluation graph and the classified moves with the Engine's move. Starts Game Analysis and
 * shows its progress.
 */
@Component({
  selector: 'app-assessment-card',
  imports: [EvalGraph, DatePipe, DecimalPipe],
  template: `
    <section class="card assessment-card" aria-label="Game Analysis" data-testid="assessment-card">
      <header class="card-head"><h2>Game Analysis</h2></header>

      @if (running(); as r) {
        <div class="analysis-progress" data-testid="analysis-progress">
          <span>Analysing… {{ r.done }} of {{ r.total || '?' }} positions</span>
          <progress [value]="r.done" [max]="r.total || 1"></progress>
        </div>
      } @else if (waiting()) {
        <p class="assessment-note" data-testid="analysis-waiting">Waiting in the analysis queue.</p>
      }
      @if (error()) { <p class="assessment-note error" role="alert" data-testid="analysis-error">{{ error() }}</p> }

      @if (assessment(); as a) {
        <div class="accuracy" data-testid="accuracy">
          <div [class.owner]="ownerColour() === 'white'"><span class="colour-dot"></span>White
            <strong class="mono" data-testid="accuracy-white">{{ a.whiteAccuracy === null ? '–' : (a.whiteAccuracy | number: '1.1-1') + '%' }}</strong></div>
          <div [class.owner]="ownerColour() === 'black'"><span class="colour-dot black"></span>Black
            <strong class="mono" data-testid="accuracy-black">{{ a.blackAccuracy === null ? '–' : (a.blackAccuracy | number: '1.1-1') + '%' }}</strong></div>
          <span class="accuracy-label">Accuracy</span>
        </div>
        <app-eval-graph [positions]="a.positions" [moves]="a.moves" [currentPly]="currentPly()" (select)="goToPly.emit($event)" />

        @if (classified().length) {
          <ul class="classified" data-testid="classified-moves">
            @for (m of classified(); track m.ply) {
              <li>
                <button type="button" class="classified-move" [class.current]="m.nodeId !== null && m.nodeId === currentId()"
                        [attr.data-mark]="m.classification" data-testid="classified-move" [disabled]="m.nodeId === null"
                        (click)="goToPly.emit(m.ply)">
                  <span class="mark-dot"></span>
                  <span class="mono">{{ label(m.ply, m.san) }}</span>
                  <span class="classified-kind">{{ m.classification }}</span>
                  @if (m.bestSan) { <span class="classified-best">best <span class="mono" data-testid="best-move">{{ label(m.ply, m.bestSan) }}</span></span> }
                </button>
              </li>
            }
          </ul>
        } @else {
          <p class="assessment-note">No inaccuracies, mistakes or blunders.</p>
        }
        @if (selected(); as m) {
          <p class="best-line" data-testid="best-line">Instead of <span class="mono">{{ label(m.ply, m.san) }}</span>, the Engine
            plays <span class="mono">{{ bestLine(m) }}</span></p>
        }
        @if (a.stale) {
          <p class="assessment-note warn" data-testid="assessment-stale">The moves have changed since this analysis. Analyse again to bring it up to date.</p>
        }
        <p class="assessment-meta" data-testid="assessment-meta">
          {{ a.engineName }} · {{ a.secondsPerMove }} s per position · {{ a.threads }} {{ a.threads === 1 ? 'thread' : 'threads' }} · {{ a.hashMb }} MB hash ·
          {{ a.analysedAt + 'Z' | date: 'mediumDate' }}
        </p>
      } @else if (!running() && !waiting()) {
        <p class="assessment-note">The Engine goes through every move of the main line and marks inaccuracies, mistakes and blunders.
          Your own Annotations stay untouched.</p>
      }

      @if (!running() && !waiting()) {
        <form class="analysis-start" (submit)="$event.preventDefault(); start(seconds.value)">
          <label for="analysis-seconds">Time per position</label>
          <input #seconds id="analysis-seconds" class="elo" type="number" min="0.1" max="600" step="0.1" [value]="defaultSeconds()" /> s
          <button type="submit" class="primary" data-testid="analyse-game">{{ assessment() ? 'Analyse again' : 'Analyse Game' }}</button>
        </form>
      }
    </section>
  `,
})
export class AssessmentCard {
  private readonly api = inject(Api);

  readonly gameId = input.required<number>();
  readonly assessment = input<EngineAssessment | null>(null);
  readonly queue = input.required<AnalysisQueue>();
  readonly error = input('');
  readonly ownerColour = input<OwnerColour>('white');
  /** The move on the board. */
  readonly currentId = input<number | null>(null);
  readonly analyse = output<number | undefined>();
  readonly goToPly = output<number>();

  protected readonly defaultSeconds = signal(2);

  constructor() {
    this.api.getEngines().subscribe((list) => list[0] && this.defaultSeconds.set(list[0].analysisSecondsPerMove));
  }

  protected readonly running = computed(() => {
    const r = this.queue().running;
    return r && r.gameId === this.gameId() ? r : null;
  });
  protected readonly waiting = computed(() => this.queue().queued.includes(this.gameId()));
  protected readonly classified = computed(() => (this.assessment()?.moves ?? []).filter((m) => m.classification));
  protected readonly currentPly = computed(() => {
    const id = this.currentId();
    if (id === null) return 0;
    return this.assessment()?.moves.find((m) => m.nodeId === id)?.ply ?? null;
  });
  /** The classified move on the board, if any. */
  protected readonly selected = computed(() => {
    const id = this.currentId();
    return this.classified().find((m) => m.nodeId !== null && m.nodeId === id) ?? null;
  });

  /** "3.Qh5" or "6...Nf6" for the move at a ply. */
  protected label(ply: number, san: string): string {
    const number = Math.ceil(ply / 2);
    return ply % 2 === 1 ? `${number}.${san}` : `${number}...${san}`;
  }

  /** The Engine's line in place of a move, numbered: "6...g6 7.Qf3 Nf6". */
  protected bestLine(move: AssessedMove): string {
    return move.bestLineSan
      .map((san, i) => {
        const ply = move.ply + i;
        if (i === 0) return this.label(ply, san);
        return ply % 2 === 1 ? this.label(ply, san) : san;
      })
      .join(' ');
  }

  protected start(seconds: string) {
    const value = Number(seconds);
    this.analyse.emit(value > 0 ? value : undefined);
  }
}
