import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../api/api';

@Component({
  selector: 'app-settings-page',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(Api);
  protected readonly ownerName = signal('');
  protected readonly status = signal('');
  private saveTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.api.getSettings().subscribe((s) => this.ownerName.set(s.ownerName));
  }

  protected onNameInput(value: string) {
    this.ownerName.set(value);
    this.status.set('');
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.save(), 400);
  }

  protected save() {
    clearTimeout(this.saveTimer);
    this.api.saveSettings({ ownerName: this.ownerName() }).subscribe({
      next: () => this.status.set('Saved'),
      error: (e) => this.status.set(problemMessage(e)),
    });
  }
}
