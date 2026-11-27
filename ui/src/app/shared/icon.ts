import { Component, computed, input } from '@angular/core';

/** Drawn on a 20×20 grid with a 1.6 stroke, round caps; one weight for the whole app. */
const PATHS = {
  first: 'M5 4v12M15 5l-6 5 6 5',
  back: 'M12.5 5l-5 5 5 5',
  forward: 'M7.5 5l5 5-5 5',
  last: 'M15 4v12M5 5l6 5-6 5',
  flip: 'M6 3v12M3.5 12.5 6 15l2.5-2.5M14 17V5M11.5 7.5 14 5l2.5 2.5',
  search: 'M8.75 14.5a5.75 5.75 0 1 0 0-11.5 5.75 5.75 0 0 0 0 11.5ZM13 13l4 4',
  book: 'M4 4.5h4.5a1.5 1.5 0 0 1 1.5 1.5v10a1.5 1.5 0 0 0-1.5-1.5H4ZM16 4.5h-4.5A1.5 1.5 0 0 0 10 6v10a1.5 1.5 0 0 1 1.5-1.5H16Z',
  export: 'M10 3v9M6.5 8.5 10 12l3.5-3.5M4 13.5V16h12v-2.5',
  chevron: 'M6 8l4 4 4-4',
  close: 'M5.5 5.5l9 9M14.5 5.5l-9 9',
  plus: 'M10 4.5v11M4.5 10h11',
} as const;

export type IconName = keyof typeof PATHS;

/** An inline stroke icon in the current text colour. Decorative: the button around it carries the name. */
@Component({
  selector: 'app-icon',
  host: { class: 'icon', 'aria-hidden': 'true' },
  template: `
    <svg viewBox="0 0 20 20" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6"
         stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="d()" /></svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  protected readonly d = computed(() => PATHS[this.name()]);
}
