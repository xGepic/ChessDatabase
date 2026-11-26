import { Component, computed, input } from '@angular/core';
import { AnalysisLine } from '../api/api';
import { scoreLabel, whitePercent } from './scores';

/** A vertical bar beside the board: White's share of the winning chances, from the Engine's best line. */
@Component({
  selector: 'app-eval-bar',
  template: `
    <div class="eval-bar" [class.idle]="!line()" [class.flipped]="orientation() === 'black'"
         role="meter" aria-label="Evaluation" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="percent()"
         [attr.aria-valuetext]="label()" data-testid="eval-bar">
      <div class="eval-white" [style.height.%]="percent()"></div>
      @if (line()) { <span class="eval-label mono" [class.white-side]="percent() >= 50">{{ label() }}</span> }
    </div>
  `,
})
export class EvalBar {
  readonly line = input<AnalysisLine | null>(null);
  readonly orientation = input<'white' | 'black'>('white');

  protected readonly percent = computed(() => {
    const line = this.line();
    return line ? Math.round(whitePercent(line) * 10) / 10 : 50;
  });
  protected readonly label = computed(() => {
    const line = this.line();
    return line ? scoreLabel(line) : '';
  });
}
