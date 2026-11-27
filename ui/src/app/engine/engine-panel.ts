import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AnalysisLine, AnalysisUpdate, Engine } from '../api/api';
import { nextPly, numberedLine, scoreLabel } from './scores';

/**
 * An Engine's best lines for the board position. Clicking a line plays it. The main panel has the
 * on/off switch and picks an Engine to compare with; a secondary panel shows that Engine's lines
 * beside it and can be closed.
 */
@Component({
  selector: 'app-engine-panel',
  imports: [RouterLink],
  template: `
    <section class="card engine-card" [attr.aria-label]="name() || 'Engine'" [attr.data-testid]="secondary() ? 'compare-panel' : 'engine-panel'">
      <header class="engine-head">
        @if (!secondary()) {
          <button type="button" class="engine-switch" role="switch" data-testid="engine-toggle"
                  [attr.aria-checked]="on()" [class.on]="on()" (click)="toggle.emit()" title="Engine on/off (E)">
            <span class="knob"></span>
          </button>
        }
        <span class="engine-title" data-testid="engine-title">{{ name() || 'Engine' }}</span>
        @if (on() && depth()) { <span class="engine-depth" data-testid="engine-depth">depth {{ depth() }}</span> }
        <span class="spacer"></span>
        @if (on() && update()?.lines?.length) {
          <span class="engine-eval mono" data-testid="engine-eval">{{ scoreLabel(update()!.lines[0]) }}</span>
        }
        @if (secondary()) {
          <button type="button" class="link-button" data-testid="compare-close" (click)="compare.emit(null)"
                  [attr.aria-label]="'Stop comparing with ' + name()" title="Stop comparing">✕</button>
        } @else if (compareOptions().length) {
          <select class="compare-select" data-testid="compare-engine" aria-label="Compare with another engine"
                  (change)="onCompare($event)">
            <option value="" [selected]="compareId() === null">Compare with…</option>
            @for (e of compareOptions(); track e.id) { <option [value]="e.id" [selected]="e.id === compareId()">{{ e.name }}</option> }
          </select>
        }
      </header>
      @if (error()) {
        <p class="engine-message error" role="alert" data-testid="engine-error">{{ error() }}
          @if (error().includes('Settings')) { <a routerLink="/settings">Open Settings</a> }
        </p>
      } @else if (on()) {
        @if (update(); as u) {
          @if (u.lines.length === 0) {
            <p class="engine-message">No legal moves in this position.</p>
          }
          <ol class="engine-lines">
            @for (line of u.lines; track line.rank) {
              <li>
                <button type="button" class="engine-line" data-testid="engine-line" (click)="play.emit(line)"
                        title="Add this line to the Game as a Variation">
                  <span class="engine-score mono">{{ scoreLabel(line) }}</span>
                  <span class="engine-moves mono">{{ numberedLine(nextPly(fen()), line.san) }}</span>
                </button>
              </li>
            }
          </ol>
        } @else {
          <p class="engine-message">Thinking…</p>
        }
      } @else if (secondary()) {
        <p class="engine-message">Starts with the Engine switch above.</p>
      } @else {
        <p class="engine-message">Switch the Engine on to see its best lines here.</p>
      }
    </section>
  `,
})
export class EnginePanel {
  readonly on = input.required<boolean>();
  readonly update = input<AnalysisUpdate | null>(null);
  readonly error = input('');
  /** The position the lines start from. */
  readonly fen = input.required<string>();
  /** The Engine's name, e.g. Stockfish. */
  readonly name = input('');
  /** A panel for the Engine compared with the main one. */
  readonly secondary = input(false);
  /** Engines the main panel can compare with, and the one it does. */
  readonly compareOptions = input<Engine[]>([]);
  readonly compareId = input<number | null>(null);
  readonly toggle = output<void>();
  readonly play = output<AnalysisLine>();
  /** An Engine to compare with; null for none. */
  readonly compare = output<number | null>();

  protected onCompare(event: Event) {
    const value = (event.target as HTMLSelectElement).value;
    this.compare.emit(value ? Number(value) : null);
  }

  protected readonly scoreLabel = scoreLabel;
  protected readonly numberedLine = numberedLine;
  protected readonly nextPly = nextPly;
  protected readonly depth = computed(() => this.update()?.lines[0]?.depth ?? null);
}
