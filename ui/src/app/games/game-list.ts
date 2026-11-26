import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { GamesApi } from '../api/games-api';

@Component({
  selector: 'app-game-list',
  templateUrl: './game-list.html',
})
export class GameList {
  private readonly api = inject(GamesApi);
  protected readonly games = toSignal(this.api.list());
}
