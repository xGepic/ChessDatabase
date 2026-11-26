import { Component, inject, signal } from '@angular/core';
import { Api, problemMessage } from '../api/api';

@Component({
  selector: 'app-settings-page',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(Api);
  protected readonly ownerName = signal('');
  protected readonly aliases = signal<string[]>([]);
  protected readonly status = signal('');
  private saveTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.api.getSettings().subscribe((s) => {
      this.ownerName.set(s.ownerName);
      this.aliases.set(s.aliases);
    });
  }

  protected onNameInput(value: string) {
    this.ownerName.set(value);
    this.status.set('');
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.save(), 400);
  }

  protected addAlias(input: HTMLInputElement) {
    const alias = input.value.trim();
    input.value = '';
    if (!alias || this.aliases().some((a) => a.toLocaleLowerCase() === alias.toLocaleLowerCase())) return;
    this.aliases.update((list) => [...list, alias]);
    this.save();
  }

  protected removeAlias(alias: string) {
    this.aliases.update((list) => list.filter((a) => a !== alias));
    this.save();
  }

  protected save() {
    clearTimeout(this.saveTimer);
    this.api.saveSettings({ ownerName: this.ownerName(), aliases: this.aliases() }).subscribe({
      next: (s) => {
        this.aliases.set(s.aliases);
        this.status.set('Saved');
      },
      error: (e) => this.status.set(problemMessage(e)),
    });
  }
}
