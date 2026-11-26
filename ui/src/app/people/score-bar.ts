import { Component, input } from '@angular/core';
import { Score } from '../api/api';

/** Won, drawn and lost as one bar, from the Owner's side. */
@Component({
  selector: 'app-score-bar',
  template: `
    <div class="score-bar" role="img" [attr.aria-label]="score().wins + ' won, ' + score().draws + ' drawn, ' + score().losses + ' lost'"
         [title]="score().wins + ' won, ' + score().draws + ' drawn, ' + score().losses + ' lost'">
      <span class="won" [style.flex-grow]="score().wins"></span>
      <span class="drawn" [style.flex-grow]="score().draws"></span>
      <span class="lost" [style.flex-grow]="score().losses"></span>
    </div>
  `,
})
export class ScoreBar {
  readonly score = input.required<Score>();
}
