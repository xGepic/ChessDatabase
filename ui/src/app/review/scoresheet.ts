import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { Classification, GameResult, MoveNode, MoveTreeView, resultLabel } from '../api/api';
import { annotatedSan, buildSheet, isOnMainLine } from './notation';

export type TreeAction = 'promote' | 'delete-variation' | 'delete-from-here';

/**
 * The notation as a scoresheet: numbered rows with White and Black columns. Variations sit as
 * indented lines under the move they branch from. Right-click a move for tree operations.
 */
@Component({
  selector: 'app-scoresheet',
  host: {
    '(document:click)': 'closeMenu()',
    '(document:keydown.escape)': 'closeMenu()',
  },
  template: `
    <div class="sheet" data-testid="notation">
      @for (item of items(); track item.key) {
        @if (item.kind === 'row') {
          <div class="sheet-row">
            <span class="move-no">{{ item.number }}.</span>
            @if (item.white; as w) {
              <ng-container *ngTemplateOutlet="moveButton; context: { $implicit: w, label: annotatedSan(w) }" />
            } @else {
              <span class="move ellipsis">…</span>
            }
            @if (item.black; as b) {
              <ng-container *ngTemplateOutlet="moveButton; context: { $implicit: b, label: annotatedSan(b) }" />
            } @else {
              <span class="move"></span>
            }
          </div>
        } @else if (item.kind === 'comment') {
          <p class="sheet-comment" data-testid="comment">{{ item.text }}</p>
        } @else {
          <div class="variations">
            @for (line of item.lines; track $index) {
              <div class="variation" data-testid="variation">
                @for (token of line; track $index) {
                  @if (!$first && token.kind !== 'close' && line[$index - 1].kind !== 'open') {{{ ' ' }}}
                  @switch (token.kind) {
                    @case ('move') {
                      <ng-container *ngTemplateOutlet="moveButton; context: { $implicit: token.node, label: token.label }" />
                    }
                    @case ('comment') { <span class="inline-comment" data-testid="comment">{{ token.text }}</span> }
                    @case ('open') { <span class="paren">(</span> }
                    @case ('close') { <span class="paren">)</span> }
                  }
                }
              </div>
            }
          </div>
        }
      } @empty {
        <p class="sheet-empty">Play the moves on the board.</p>
      }
      @if (items().length && game().result) {
        <div class="sheet-end">{{ resultLabel(game().result!) }}</div>
      }
    </div>

    <ng-template #moveButton let-node let-label="label">
      <button type="button" class="move" [class.current]="node.id === currentId()" [attr.data-node]="node.id"
              [attr.data-mark]="marks().get(node.id) ?? null" [attr.title]="markTitle(node.id)"
              data-testid="move" (click)="select.emit(node.id)" (contextmenu)="openMenu($event, node)">{{ label }}</button>
    </ng-template>

    @if (menu(); as m) {
      <div class="context-menu" role="menu" data-testid="move-menu" [style.left.px]="m.x" [style.top.px]="m.y"
           (click)="$event.stopPropagation()">
        <button type="button" role="menuitem" data-testid="menu-promote" [disabled]="m.onMainLine"
                (click)="act('promote', m.node)">Promote to main line</button>
        <button type="button" role="menuitem" data-testid="menu-delete-variation" [disabled]="m.onMainLine"
                (click)="act('delete-variation', m.node)">Delete Variation</button>
        <button type="button" role="menuitem" data-testid="menu-delete-from-here"
                (click)="act('delete-from-here', m.node)">Delete from here</button>
      </div>
    }
  `,
  imports: [NgTemplateOutlet],
})
export class Scoresheet {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** A Game (its result ends the sheet) or any other move tree, such as a Repertoire. */
  readonly game = input.required<MoveTreeView & { result?: GameResult }>();
  readonly currentId = input<number | null>(null);
  /** Move Classifications from the Engine Assessment, by move. Shown as a coloured mark, never as a symbol. */
  readonly marks = input<ReadonlyMap<number, Classification>>(new Map());
  readonly select = output<number | null>();
  readonly action = output<{ action: TreeAction; node: MoveNode }>();

  protected readonly resultLabel = resultLabel;
  protected readonly annotatedSan = annotatedSan;
  protected readonly items = computed(() => buildSheet(this.game()));
  protected readonly menu = signal<{ x: number; y: number; node: MoveNode; onMainLine: boolean } | null>(null);

  constructor() {
    // Keep the current move in view while stepping through a long Game.
    effect(() => {
      const id = this.currentId();
      this.items();
      queueMicrotask(() =>
        this.host.nativeElement.querySelector(`[data-node="${id}"]`)?.scrollIntoView({ block: 'nearest' }),
      );
    });
  }

  protected markTitle(id: number): string | null {
    const mark = this.marks().get(id);
    return mark ? `Engine: ${mark}` : null;
  }

  protected openMenu(event: MouseEvent, node: MoveNode) {
    event.preventDefault();
    event.stopPropagation();
    this.select.emit(node.id);
    this.menu.set({ x: event.clientX, y: event.clientY, node, onMainLine: isOnMainLine(this.game(), node) });
  }

  protected closeMenu() {
    this.menu.set(null);
  }

  protected act(action: TreeAction, node: MoveNode) {
    this.menu.set(null);
    this.action.emit({ action, node });
  }
}
