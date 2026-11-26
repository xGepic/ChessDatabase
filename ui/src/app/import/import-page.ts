import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, ImportStatus, ImportSummary, ImportedGame, problemMessage } from '../api/api';

const STATUS_LABELS: Record<ImportStatus, string> = {
  imported: 'Imported',
  needsReview: 'Needs review',
  duplicate: 'Duplicate',
  failed: 'Failed',
};

/** Imports PGN from a file or pasted text and shows what happened to every Game. */
@Component({
  selector: 'app-import-page',
  imports: [RouterLink],
  templateUrl: './import-page.html',
})
export class ImportPage {
  private readonly api = inject(Api);

  protected readonly pgn = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly summary = signal<ImportSummary | null>(null);
  protected readonly statusLabels = STATUS_LABELS;

  protected importText() {
    if (this.pgn().trim()) this.run(this.api.importText(this.pgn()));
  }

  protected importFile(input: HTMLInputElement) {
    const file = input.files?.[0];
    input.value = '';
    if (file) this.run(this.api.importFile(file));
  }

  /** Imports one duplicate after all and marks its row as imported. */
  protected importAnyway(row: ImportedGame) {
    if (!row.pgn) return;
    this.api.importText(row.pgn, true).subscribe({
      next: (result) => {
        const imported = result.games[0];
        this.summary.update((s) =>
          s && {
            ...s,
            imported: s.imported + result.imported,
            duplicates: s.duplicates - result.imported,
            games: s.games.map((g) =>
              g === row ? { ...g, status: imported.status, gameId: imported.gameId, reason: imported.reason, pgn: null } : g,
            ),
          },
        );
      },
      error: (e) => this.error.set(problemMessage(e)),
    });
  }

  private run(call: Observable<ImportSummary>) {
    this.busy.set(true);
    this.error.set('');
    this.summary.set(null);
    call.subscribe({
      next: (summary) => {
        this.busy.set(false);
        this.summary.set(summary);
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(problemMessage(e));
      },
    });
  }
}
