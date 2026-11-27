import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { ANALYSIS_SECONDS, AnalysisQueue, Api, EngineAssessment, OwnerColour } from '../api/api';
import { forkJoin } from 'rxjs';
import { EvalGraph } from './eval-graph';
import { numberedLine } from './scores';

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
              <li class="classified-row">
                <button type="button" class="classified-move" [class.current]="m.nodeId !== null && m.nodeId === currentId()"
                        [attr.data-mark]="m.classification" data-testid="classified-move" [disabled]="m.nodeId === null"
                        (click)="goToPly.emit(m.ply)">
                  <span class="mark-dot"></span>
                  <span class="mono">{{ numberedLine(m.ply, [m.san]) }}</span>
                  <span class="classified-kind">{{ m.classification }}</span>
                  @if (m.bestSan) { <span class="classified-best">best <span class="mono" data-testid="best-move">{{ numberedLine(m.ply, [m.bestSan]) }}</span></span> }
                </button>
                <button type="button" class="copy-move" data-testid="copy-move" [disabled]="m.nodeId === null"
                        [attr.aria-label]="'Copy ' + numberedLine(m.ply, [m.san]) + ' into the Game'"
                        title="Copy into the Game: the symbol and the Engine line as a Variation" (click)="copy.emit(m.ply)">Copy</button>
              </li>
            }
          </ul>
        } @else {
          <p class="assessment-note">No inaccuracies, mistakes or blunders.</p>
        }
        @if (selected(); as m) {
          <div class="best-line" data-testid="best-line">
            <p>Instead of <span class="mono">{{ numberedLine(m.ply, [m.san]) }}</span>, the Engine plays <span class="mono">{{ numberedLine(m.ply, m.bestLineSan) }}</span></p>
          </div>
        }
        @if (hasMistakes()) {
          <div class="copy-all">
            <button type="button" data-testid="copy-all" (click)="copyAll.emit()"
                    title="Adds ? and ?? where you have no symbol yet, and the Engine lines as Variations">Copy all mistakes and blunders</button>
            @if (copyMessage()) { <span class="status" role="status" data-testid="copy-status">{{ copyMessage() }}</span> }
          </div>
        } @else if (copyMessage()) {
          <p class="assessment-note" role="status" data-testid="copy-status">{{ copyMessage() }}</p>
        }
        @if (a.stale) {
          <p class="assessment-note warn" data-testid="assessment-stale">The moves have changed since this analysis. Analyse again to bring it up to date.</p>
        }
        <p class="assessment-meta" data-testid="assessment-meta">
          {{ a.engineName }} · {{ a.secondsPerMove }} s per position · {{ a.threads }} {{ a.threads === 1 ? 'thread' : 'threads' }} · {{ a.hashMb }} MB hash ·
          {{ a.analysedAt | date: 'mediumDate' }}
        </p>
      } @else if (!running() && !waiting()) {
        <p class="assessment-note">The Engine goes through every move of the main line and marks inaccuracies, mistakes and blunders.
          Your own Annotations stay untouched.</p>
      }

      @if (!running() && !waiting()) {
        <form class="analysis-start" (submit)="$event.preventDefault(); start(secondsInput.value)">
          <label for="analysis-seconds">Time per position</label>
          <input #secondsInput id="analysis-seconds" class="elo" type="number" [min]="seconds.min" [max]="seconds.max" step="0.1"
                 [value]="defaultSeconds()" /> s
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
  /** Copy one classified move (by ply) into the Game. */
  readonly copy = output<number>();
  readonly copyAll = output<void>();
  /** The outcome of the last copy. */
  readonly copyMessage = input('');

  protected readonly defaultSeconds = signal(2);
  protected readonly seconds = ANALYSIS_SECONDS;
  protected readonly numberedLine = numberedLine;

  constructor() {
    // The default time is the Game Analysis engine's.
    forkJoin([this.api.getEngines(), this.api.getEngineSelection()]).subscribe(([list, selection]) => {
      const engine = list.find((e) => e.id === selection.analysisEngineId);
      if (engine) this.defaultSeconds.set(engine.analysisSecondsPerMove);
    });
  }

  protected readonly running = computed(() => {
    const r = this.queue().running;
    return r && r.gameId === this.gameId() ? r : null;
  });
  protected readonly waiting = computed(() => this.queue().queued.includes(this.gameId()));
  protected readonly classified = computed(() => (this.assessment()?.moves ?? []).filter((m) => m.classification));
  protected readonly hasMistakes = computed(() =>
    this.classified().some((m) => m.nodeId !== null && (m.classification === 'mistake' || m.classification === 'blunder')),
  );
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

  protected start(seconds: string) {
    const value = Number(seconds);
    this.analyse.emit(value > 0 ? value : undefined);
  }
}
