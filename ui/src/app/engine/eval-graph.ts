import { Component, computed, input, output } from '@angular/core';
import { AssessedMove, AssessedPosition } from '../api/api';

const WIDTH = 600;
const HEIGHT = 120;

/**
 * White's winning chance across the whole Game: White's share fills from the bottom. Classified
 * moves get a coloured dot. Clicking anywhere jumps to the nearest position.
 */
@Component({
  selector: 'app-eval-graph',
  template: `
    <svg class="eval-graph" [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" role="img"
         aria-label="Evaluation graph: click to go to a position" data-testid="eval-graph" (click)="onClick($event)">
      <rect class="graph-black" x="0" y="0" [attr.width]="width" [attr.height]="height" />
      <path class="graph-white" [attr.d]="area()" />
      <line class="graph-mid" x1="0" [attr.x2]="width" [attr.y1]="height / 2" [attr.y2]="height / 2"
            vector-effect="non-scaling-stroke" />
      @if (currentX() !== null) {
        <line class="graph-current" [attr.x1]="currentX()" [attr.x2]="currentX()" y1="0" [attr.y2]="height"
              vector-effect="non-scaling-stroke" />
      }
      @for (dot of dots(); track dot.ply) {
        <!-- a zero-length line with round caps stays a circle however the graph is stretched -->
        <line [attr.class]="'graph-dot ' + dot.mark" [attr.x1]="dot.x" [attr.x2]="dot.x" [attr.y1]="dot.y" [attr.y2]="dot.y"
              vector-effect="non-scaling-stroke" />
      }
    </svg>
  `,
})
export class EvalGraph {
  readonly positions = input.required<AssessedPosition[]>();
  readonly moves = input.required<AssessedMove[]>();
  /** The ply on the board, if it is on the analysed line. */
  readonly currentPly = input<number | null>(null);
  readonly select = output<number>();

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;

  private readonly step = computed(() => WIDTH / Math.max(1, this.positions().length - 1));
  private y(percent: number) {
    return HEIGHT - (percent / 100) * HEIGHT;
  }

  protected readonly area = computed(() => {
    const points = this.positions().map((p, i) => `${(i * this.step()).toFixed(1)},${this.y(p.whitePercent).toFixed(1)}`);
    if (!points.length) return '';
    return `M0,${HEIGHT} L${points.join(' L')} L${((this.positions().length - 1) * this.step()).toFixed(1)},${HEIGHT} Z`;
  });

  protected readonly dots = computed(() =>
    this.moves()
      .filter((m) => m.classification)
      .map((m) => ({
        ply: m.ply,
        mark: m.classification!,
        x: m.ply * this.step(),
        y: this.y(this.positions()[m.ply]?.whitePercent ?? 50),
      })),
  );

  protected readonly currentX = computed(() => {
    const ply = this.currentPly();
    return ply === null ? null : ply * this.step();
  });

  protected onClick(event: MouseEvent) {
    const svg = event.currentTarget as SVGSVGElement;
    const box = svg.getBoundingClientRect();
    const x = ((event.clientX - box.left) / box.width) * WIDTH;
    const ply = Math.round(x / this.step());
    this.select.emit(Math.max(0, Math.min(this.positions().length - 1, ply)));
  }
}
