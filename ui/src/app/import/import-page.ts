import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Api, ImportStatus, ImportSummary, problemMessage } from '../api/api';

const STATUS_LABELS: Record<ImportStatus, string> = {
  imported: 'Imported',
  needsReview: 'Needs review',
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
