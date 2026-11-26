import { Component, computed, input, output, signal } from '@angular/core';

/** A value at a (possibly partial) date: "2024", "2024-03" or "2024-03-05". */
export interface TimePoint {
  date: string;
  value: number;
  /** What a click on the point opens (a Game id). */
  id: number;
}

const WIDTH = 560;
const HEIGHT = 180;
const PAD = { top: 12, right: 16, bottom: 24, left: 44 };

/** A partial date placed in the middle of the period it names, as a time value. */
function timeOf(date: string): number {
  const [y, m, d] = date.split('-').map(Number);
  if (!m) return Date.UTC(y, 6, 1);
  if (!d) return Date.UTC(y, m - 1, 15);
  return Date.UTC(y, m - 1, d);
}

/**
 * One series over time as a line with markers: the rating history of a Player, the Owner's
 * Accuracy. Recessive axes, a crosshair and tooltip on hover, a click on a point opens its Game,
 * and the same figures as a table for reading without the chart.
 */
@Component({
  selector: 'app-time-chart',
  template: `
    @if (points().length === 0) {
      <p class="hint chart-empty">{{ emptyText() }}</p>
    } @else {
      <div class="time-chart" (mouseleave)="hover.set(null)">
        <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img" [attr.aria-label]="label()" data-testid="time-chart"
             (mousemove)="onMove($event)" (click)="onClick()">
          @for (t of yTicks(); track t.value) {
            <line class="chart-grid" [attr.x1]="pad.left" [attr.x2]="width - pad.right" [attr.y1]="t.y" [attr.y2]="t.y" />
            <text class="chart-tick" [attr.x]="pad.left - 6" [attr.y]="t.y + 4" text-anchor="end">{{ t.value }}</text>
          }
          @for (t of xTicks(); track t.label) {
            <text class="chart-tick" [attr.x]="t.x" [attr.y]="height - 6" [attr.text-anchor]="t.anchor">{{ t.label }}</text>
          }
          <path class="chart-line" [attr.d]="path()" />
          @if (hover(); as h) {
            <line class="chart-crosshair" [attr.x1]="h.x" [attr.x2]="h.x" [attr.y1]="pad.top" [attr.y2]="height - pad.bottom" />
          }
          @for (p of placed(); track p.id + '-' + $index) {
            <circle class="chart-point" [class.on]="hover()?.id === p.id && hover()?.date === p.date" [attr.cx]="p.x" [attr.cy]="p.y" r="4" />
          }
        </svg>
        @if (hover(); as h) {
          <div class="chart-tooltip" role="status" [style.left.%]="(h.x / width) * 100" [style.top.%]="(h.y / height) * 100">
            <span class="mono">{{ h.date }}</span> · <strong class="mono">{{ format(h.value) }}</strong>
          </div>
        }
      </div>
      <details class="chart-table">
        <summary>Show as table</summary>
        <table>
          <thead><tr><th>Date</th><th class="num">{{ valueName() }}</th></tr></thead>
          <tbody>
            @for (p of points(); track $index) {
              <tr><td class="mono">{{ p.date }}</td><td class="num mono">{{ format(p.value) }}</td></tr>
            }
          </tbody>
        </table>
      </details>
    }
  `,
})
export class TimeChart {
  readonly points = input.required<TimePoint[]>();
  /** The chart's accessible name, e.g. "Rating history". */
  readonly label = input.required<string>();
  /** Column heading in the table view, e.g. "Elo". */
  readonly valueName = input('Value');
  readonly emptyText = input('Nothing to chart yet.');
  /** Decimals shown for values. */
  readonly decimals = input(0);
  /** A point was clicked: its id. */
  readonly open = output<number>();

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly pad = PAD;
  protected readonly hover = signal<{ x: number; y: number; date: string; value: number; id: number } | null>(null);

  private readonly domain = computed(() => {
    const pts = this.points();
    const times = pts.map((p) => timeOf(p.date));
    const values = pts.map((p) => p.value);
    let [lo, hi] = [Math.min(...values), Math.max(...values)];
    if (lo === hi) [lo, hi] = [lo - 10, hi + 10];
    const margin = (hi - lo) * 0.1;
    let [t0, t1] = [Math.min(...times), Math.max(...times)];
    if (t0 === t1) [t0, t1] = [t0 - 86_400_000 * 30, t1 + 86_400_000 * 30];
    return { t0, t1, lo: lo - margin, hi: hi + margin };
  });

  protected readonly placed = computed(() => {
    const { t0, t1, lo, hi } = this.domain();
    const w = WIDTH - PAD.left - PAD.right;
    const h = HEIGHT - PAD.top - PAD.bottom;
    return this.points().map((p) => ({
      ...p,
      x: PAD.left + ((timeOf(p.date) - t0) / (t1 - t0)) * w,
      y: PAD.top + (1 - (p.value - lo) / (hi - lo)) * h,
    }));
  });

  protected readonly path = computed(() =>
    this.placed().map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' '),
  );

  /** Three round values across the range. */
  protected readonly yTicks = computed(() => {
    const { lo, hi } = this.domain();
    const h = HEIGHT - PAD.top - PAD.bottom;
    const step = niceStep((hi - lo) / 3);
    const ticks = [];
    for (let v = Math.ceil(lo / step) * step; v <= hi; v += step)
      ticks.push({ value: Number(v.toFixed(2)), y: PAD.top + (1 - (v - lo) / (hi - lo)) * h });
    return ticks;
  });

  /** The first and last dates. */
  protected readonly xTicks = computed(() => {
    const placed = this.placed();
    if (!placed.length) return [];
    const first = placed[0];
    const last = placed[placed.length - 1];
    return first.date === last.date
      ? [{ label: first.date, x: first.x, anchor: 'middle' }]
      : [
          { label: first.date, x: first.x, anchor: 'start' },
          { label: last.date, x: last.x, anchor: 'end' },
        ];
  });

  protected format(value: number): string {
    return value.toFixed(this.decimals());
  }

  /** The nearest point to the pointer, horizontally. */
  protected onMove(event: MouseEvent) {
    const svg = event.currentTarget as SVGSVGElement;
    const box = svg.getBoundingClientRect();
    const x = ((event.clientX - box.left) / box.width) * WIDTH;
    let best = this.placed()[0];
    for (const p of this.placed()) if (Math.abs(p.x - x) < Math.abs(best.x - x)) best = p;
    this.hover.set(best ? { x: best.x, y: best.y, date: best.date, value: best.value, id: best.id } : null);
  }

  protected onClick() {
    const h = this.hover();
    if (h) this.open.emit(h.id);
  }
}

/** 1, 2 or 5 times a power of ten, at least the given size. */
function niceStep(raw: number): number {
  const power = 10 ** Math.floor(Math.log10(Math.max(raw, 1e-9)));
  return [1, 2, 5, 10].map((m) => m * power).find((s) => s >= raw) ?? 10 * power;
}
