import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { ApiErrorModalComponent } from './core/api-error-modal.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ApiErrorModalComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
})
export class AppComponent {
  title = 'SmartNarrator';
}
