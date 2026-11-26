import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AnalysisLine, AnalysisUpdate } from '../api/api';
import { nextPly, numberedLine, scoreLabel } from './scores';

/** The Engine's best lines for the board position, with an on/off switch. Clicking a line plays it. */
@Component({
  selector: 'app-engine-panel',
  imports: [RouterLink],
  template: `
    <section class="card engine-card" aria-label="Engine">
      <header class="engine-head">
        <button type="button" class="engine-switch" role="switch" data-testid="engine-toggle"
                [attr.aria-checked]="on()" [class.on]="on()" (click)="toggle.emit()" title="Engine on/off (E)">
          <span class="knob"></span>
        </button>
        <span class="engine-title">Engine</span>
        @if (on() && depth()) { <span class="engine-depth" data-testid="engine-depth">depth {{ depth() }}</span> }
        <span class="spacer"></span>
        @if (on() && update()?.lines?.length) {
          <span class="engine-eval mono" data-testid="engine-eval">{{ scoreLabel(update()!.lines[0]) }}</span>
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
  readonly toggle = output<void>();
  readonly play = output<AnalysisLine>();

  protected readonly scoreLabel = scoreLabel;
  protected readonly numberedLine = numberedLine;
  protected readonly nextPly = nextPly;
  protected readonly depth = computed(() => this.update()?.lines[0]?.depth ?? null);
}
