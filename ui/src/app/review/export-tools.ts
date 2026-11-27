import { Component, computed, effect, input, signal } from '@angular/core';

const OPTIONS_KEY = 'chessdb.exportOptions';

interface ExportOptions {
  coordinates: boolean;
  lastMove: boolean;
  finalDiagram: boolean;
}

function loadOptions(): ExportOptions {
  const defaults: ExportOptions = { coordinates: true, lastMove: true, finalDiagram: true };
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(OPTIONS_KEY) ?? '{}') };
  } catch {
    return defaults;
  }
}

/**
 * Getting a Game out of the app: the board position as a PNG on the clipboard, and the Game as
 * PGN or PDF. The diagram and PDF options are remembered between Games.
 */
@Component({
  selector: 'app-export-tools',
  template: `
    <div class="export-tools">
      <button type="button" data-testid="copy-diagram" (click)="copyDiagram()"
              title="Copy the board position as an image, to paste into other programs">Copy diagram</button>
      <label class="tool-toggle" title="Files and ranks beside the diagram">
        <input type="checkbox" data-testid="diagram-coordinates" [checked]="options().coordinates"
               (change)="set('coordinates', $any($event.target).checked)" /> coordinates
      </label>
      <label class="tool-toggle" title="Highlight the last move in the diagram">
        <input type="checkbox" data-testid="diagram-last-move" [checked]="options().lastMove"
               (change)="set('lastMove', $any($event.target).checked)" /> last move
      </label>
      <span class="tool-divider" aria-hidden="true"></span>
      <a class="button tool-link" [href]="'/api/games/' + gameId() + '/pgn'" download data-testid="export-game">Export PGN</a>
      <a class="button tool-link" [href]="pdfUrl()" download data-testid="export-pdf"
         title="The annotated Game with diagrams at the moves marked for one">Export PDF</a>
      <label class="tool-toggle" title="End the PDF with a diagram of the final position">
        <input type="checkbox" data-testid="pdf-final-diagram" [checked]="options().finalDiagram"
               (change)="set('finalDiagram', $any($event.target).checked)" /> final position
      </label>
      @if (message()) { <span class="board-note" role="status" data-testid="diagram-message">{{ message() }}</span> }
    </div>
  `,
})
export class ExportTools {
  readonly gameId = input.required<number>();
  /** The board position and how the board shows it. */
  readonly fen = input.required<string>();
  readonly orientation = input.required<'white' | 'black'>();
  readonly lastMove = input<string | null>(null);

  protected readonly options = signal(loadOptions());
  protected readonly message = signal('');
  protected readonly pdfUrl = computed(() => `/api/games/${this.gameId()}/pdf?finalDiagram=${this.options().finalDiagram}`);

  private messageTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const options = JSON.stringify(this.options());
      try {
        localStorage.setItem(OPTIONS_KEY, options);
      } catch {
        // Storage full or unavailable: the options just aren't remembered.
      }
    });
  }

  protected set(option: keyof ExportOptions, on: boolean) {
    this.options.update((o) => ({ ...o, [option]: on }));
  }

  protected async copyDiagram() {
    const params = new URLSearchParams({
      fen: this.fen(),
      orientation: this.orientation(),
      coordinates: String(this.options().coordinates),
    });
    const lastMove = this.lastMove();
    if (this.options().lastMove && lastMove) params.set('lastMove', lastMove);
    // The image is handed over as a promise, so the copy still counts as part of the click.
    const png = fetch(`/api/diagram?${params}`).then((response) => {
      if (!response.ok) throw new Error(`The diagram could not be drawn (${response.status}).`);
      return response.blob();
    });
    try {
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': png })]);
      this.show('Diagram copied');
    } catch (e) {
      this.show(e instanceof Error && e.message ? e.message : 'The diagram could not be copied.');
    }
  }

  private show(text: string) {
    this.message.set(text);
    clearTimeout(this.messageTimer);
    this.messageTimer = setTimeout(() => this.message.set(''), 3000);
  }
}
