import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, GameResult, OwnerColour, Player, RESULTS, problemMessage } from '../api/api';
import { PlayerPicker } from '../shared/player-picker';
import { parseElo } from '../shared/elo';

/** Starts a Game: the Owner's colour, the Opponent, both Elos and the result. Moves follow on the board. */
@Component({
  selector: 'app-new-game',
  imports: [PlayerPicker],
  templateUrl: './new-game.html',
})
export class NewGame {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly results = RESULTS;
  protected readonly ownerColour = signal<OwnerColour>('white');
  protected readonly opponent = signal<Player | null>(null);
  protected readonly ownerElo = signal('');
  protected readonly opponentElo = signal('');
  protected readonly result = signal<GameResult | null>(null);
  protected readonly date = signal(new Date().toISOString().slice(0, 10));
  protected readonly error = signal('');
  protected readonly saving = signal(false);

  protected readonly ready = computed(() => !!this.opponent() && !!this.result() && !this.saving());

  protected start() {
    const opponent = this.opponent();
    const result = this.result();
    if (!opponent || !result) return;
    const ownerElo = parseElo(this.ownerElo());
    const opponentElo = parseElo(this.opponentElo());
    if (ownerElo === undefined || opponentElo === undefined) {
      this.error.set('An Elo is a number. Leave it empty or enter 0 for unrated.');
      return;
    }
    const white = this.ownerColour() === 'white';
    this.saving.set(true);
    this.api
      .createGame({
        ownerColour: this.ownerColour(),
        opponentId: opponent.id,
        whiteElo: white ? ownerElo : opponentElo,
        blackElo: white ? opponentElo : ownerElo,
        result,
        date: this.date() || null,
      })
      .subscribe({
        next: (game) => this.router.navigate(['/games', game.id]),
        error: (e) => {
          this.saving.set(false);
          this.error.set(problemMessage(e));
        },
      });
  }
}
