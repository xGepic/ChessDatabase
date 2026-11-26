import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Api, GameSummary, eloLabel, resultLabel } from '../api/api';

@Component({
  selector: 'app-game-list',
  imports: [RouterLink],
  templateUrl: './game-list.html',
})
export class GameList {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  protected readonly games = toSignal(this.api.listGames());
  protected readonly eloLabel = eloLabel;
  protected readonly resultLabel = resultLabel;

  protected open(game: GameSummary) {
    this.router.navigate(['/games', game.id]);
  }
}
