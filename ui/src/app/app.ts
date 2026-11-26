import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AnalysisStrip } from './engine/analysis-strip';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AnalysisStrip],
  templateUrl: './app.html',
})
export class App {}
