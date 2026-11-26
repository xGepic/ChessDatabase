import { Component, OnDestroy, computed, effect, inject, input, output, signal } from '@angular/core';
import { map } from 'rxjs';
import { Api, Game, GameDetails, GameResult, OwnerColour, Player, RESULTS, problemMessage } from '../api/api';
import { PlayerPicker } from '../shared/player-picker';
import { SuggestInput } from '../shared/suggest-input';
import { parseElo } from '../shared/elo';
import { sameName } from '../shared/names';

const NOTES_DEBOUNCE_MS = 500;

/** Edits a Game's details. Every change is saved at once; there is no Save button. */
@Component({
  selector: 'app-game-details',
  imports: [PlayerPicker, SuggestInput],
  template: `
    <section class="card side-card">
      <header class="card-head"><h2>Game</h2></header>
      <div class="form-grid compact">
        <span class="label">I played</span>
        <div class="segmented" role="radiogroup" aria-label="Your colour">
          <button type="button" role="radio" [attr.aria-checked]="game().ownerColour === 'white'"
                  [class.on]="game().ownerColour === 'white'" (click)="setColour('white')">White</button>
          <button type="button" role="radio" [attr.aria-checked]="game().ownerColour === 'black'"
                  [class.on]="game().ownerColour === 'black'" (click)="setColour('black')">Black</button>
        </div>

        <label for="details-opponent">Opponent</label>
        <app-player-picker inputId="details-opponent" [player]="game().opponent" (playerChange)="setOpponent($event)" />

        <label for="white-elo">White Elo</label>
        <input id="white-elo" class="elo" inputmode="numeric" placeholder="unrated" [value]="whiteElo()"
               (input)="whiteElo.set($any($event.target).value)" (change)="saveElos()" />

        <label for="black-elo">Black Elo</label>
        <input id="black-elo" class="elo" inputmode="numeric" placeholder="unrated" [value]="blackElo()"
               (input)="blackElo.set($any($event.target).value)" (change)="saveElos()" />

        <span class="label">Result</span>
        <div class="segmented" role="radiogroup" aria-label="Result">
          @for (r of results; track r.value) {
            <button type="button" role="radio" [attr.aria-checked]="game().result === r.value"
                    [class.on]="game().result === r.value" (click)="setResult(r.value)">{{ r.label }}</button>
          }
        </div>

        <label for="details-event">Event</label>
        <app-suggest-input inputId="details-event" testId="event-input" placeholder="Tournament or league"
                           [value]="game().event?.name ?? null" [search]="searchEvents" (commit)="save({ event: $event })" />

        <label for="details-site">Site</label>
        <app-suggest-input inputId="details-site" testId="site-input" placeholder="Where"
                           [value]="game().site" [search]="searchSites" (commit)="save({ site: $event })" />

        <label for="details-date">Date</label>
        <input id="details-date" data-testid="date-input" [value]="game().date ?? ''" placeholder="YYYY, YYYY-MM or YYYY-MM-DD"
               (change)="save({ date: $any($event.target).value.trim() || null })" />

        <label for="details-round">Round</label>
        <input id="details-round" class="short" data-testid="round-input" [value]="game().round ?? ''"
               (change)="save({ round: $any($event.target).value })" />

        <label for="details-time-control">Time control</label>
        <input id="details-time-control" class="short" data-testid="time-control-input" placeholder="e.g. 90+30"
               [value]="game().timeControl ?? ''" (change)="save({ timeControl: $any($event.target).value })" />

        <label for="details-tags">Tags</label>
        <div class="tags">
          @for (tag of game().tags; track tag) {
            <span class="tag" data-testid="tag">{{ tag }}
              <button type="button" class="tag-remove" [attr.aria-label]="'Remove tag ' + tag" (click)="removeTag(tag)">×</button>
            </span>
          }
          <app-suggest-input inputId="details-tags" testId="tag-input" placeholder="Add a tag" [clearOnCommit]="true"
                             [search]="searchTags" (commit)="addTag($event)" />
        </div>

        <label for="details-notes">Notes</label>
        <textarea id="details-notes" data-testid="notes-input" rows="3" placeholder="Private notes on this Game"
                  [value]="notes()" (input)="onNotesInput($any($event.target).value)" (blur)="flushNotes()"></textarea>
      </div>
      <footer class="card-foot">
        @if (error()) {
          <span class="field-error">{{ error() }}</span>
        } @else {
          <span class="hint">Changes save automatically.</span>
        }
        <span class="spacer"></span>
        @if (confirmingDelete()) {
          <span class="confirm">Delete this Game?</span>
          <button type="button" class="danger" data-testid="confirm-delete" (click)="deleted.emit()">Delete</button>
          <button type="button" (click)="confirmingDelete.set(false)">Keep</button>
        } @else {
          <button type="button" class="danger" data-testid="delete-game" (click)="confirmingDelete.set(true)">Delete Game</button>
        }
      </footer>
    </section>
  `,
})
export class GameDetailsCard implements OnDestroy {
  private readonly api = inject(Api);

  readonly game = input.required<Game>();
  readonly saved = output<Game>();
  readonly deleted = output<void>();

  protected readonly results = RESULTS;
  protected readonly whiteElo = signal('');
  protected readonly blackElo = signal('');
  protected readonly notes = signal('');
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);

  protected readonly searchEvents = (term: string) => this.api.searchEvents(term).pipe(map((l) => l.map((e) => e.name)));
  protected readonly searchSites = (term: string) => this.api.searchSites(term);
  protected readonly searchTags = (term: string) => this.api.searchTags(term).pipe(map((l) => l.map((t) => t.name)));

  private readonly savedWhiteElo = computed(() => this.game().whiteElo);
  private readonly savedBlackElo = computed(() => this.game().blackElo);
  private readonly savedNotes = computed(() => this.game().notes);
  private notesTimer: ReturnType<typeof setTimeout> | undefined;
  private notesPending = false;

  private readonly details = computed<GameDetails>(() => {
    const g = this.game();
    return {
      ownerColour: g.ownerColour,
      opponentId: g.opponent.id,
      whiteElo: g.whiteElo,
      blackElo: g.blackElo,
      result: g.result,
      date: g.date,
      event: g.event?.name ?? null,
      site: g.site,
      round: g.round,
      timeControl: g.timeControl,
      notes: g.notes,
      tags: g.tags,
    };
  });

  constructor() {
    // Only a change of the saved value resets a field, so other saves (e.g. a comment) never
    // overwrite what the Owner is typing.
    effect(() => this.whiteElo.set(this.savedWhiteElo() ? String(this.savedWhiteElo()) : ''));
    effect(() => this.blackElo.set(this.savedBlackElo() ? String(this.savedBlackElo()) : ''));
    effect(() => {
      const saved = this.savedNotes();
      if (!this.notesPending) this.notes.set(saved ?? '');
    });
  }

  ngOnDestroy() {
    this.flushNotes();
  }

  protected setColour(ownerColour: OwnerColour) {
    if (ownerColour !== this.game().ownerColour) this.save({ ownerColour });
  }

  protected setOpponent(player: Player) {
    if (player.id !== this.game().opponent.id) this.save({ opponentId: player.id });
  }

  protected setResult(result: GameResult) {
    if (result !== this.game().result) this.save({ result });
  }

  protected saveElos() {
    const whiteElo = parseElo(this.whiteElo());
    const blackElo = parseElo(this.blackElo());
    if (whiteElo === undefined || blackElo === undefined) {
      this.error.set('An Elo is a number. Leave it empty or enter 0 for unrated.');
      return;
    }
    this.save({ whiteElo, blackElo });
  }

  protected addTag(tag: string) {
    const tags = this.game().tags;
    if (!tags.some((t) => sameName(t, tag))) this.save({ tags: [...tags, tag] });
  }

  protected removeTag(tag: string) {
    this.save({ tags: this.game().tags.filter((t) => t !== tag) });
  }

  protected onNotesInput(text: string) {
    this.notes.set(text);
    this.notesPending = true;
    clearTimeout(this.notesTimer);
    this.notesTimer = setTimeout(() => this.flushNotes(), NOTES_DEBOUNCE_MS);
  }

  protected flushNotes() {
    clearTimeout(this.notesTimer);
    if (!this.notesPending) return;
    this.notesPending = false;
    this.save({ notes: this.notes() });
  }

  protected save(change: Partial<GameDetails>) {
    this.error.set('');
    this.api.updateGame(this.game().id, { ...this.details(), ...change }).subscribe({
      next: (game) => this.saved.emit(game),
      error: (e) => this.error.set(problemMessage(e)),
    });
  }
}
