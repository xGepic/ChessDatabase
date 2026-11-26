import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Switches between Repertoire Training and Mistake Training. */
@Component({
  selector: 'app-training-nav',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="segmented training-nav" aria-label="Training">
      <a class="button" routerLink="/training" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }"
         data-testid="training-repertoire">Repertoire</a>
      <a class="button" routerLink="/training/mistakes" routerLinkActive="on" data-testid="training-mistakes">Mistakes</a>
    </nav>
  `,
})
export class TrainingNav {}
