import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-work-layout',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterOutlet],
  template: `
    <nav class="toolbar">
      <a routerLink="/works">Works</a>
      <span class="sep">/</span>
      <span>Editor</span>
    </nav>

    <nav class="tabs">
      <a routerLink="analysis" routerLinkActive="active">Analysis</a>
      <a routerLink="timeline" routerLinkActive="active">Summary</a>
      <a routerLink="characters" routerLinkActive="active">Characters</a>
      <a routerLink="audio" routerLinkActive="active">Audio</a>
      <a routerLink="profiles" routerLinkActive="active">Profiles</a>
    </nav>

    <main class="shell">
      <router-outlet />
    </main>
  `,
  styles: [
    `
      .toolbar {
        margin-bottom: 0.75rem;
        font-size: 0.95rem;
      }
      .toolbar a {
        color: inherit;
      }
      .sep {
        margin: 0 0.35rem;
        opacity: 0.5;
      }
      .tabs {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        margin-bottom: 1rem;
      }
      .tabs a {
        padding: 0.35rem 0.65rem;
        border-radius: 999px;
        background: var(--sn-nav-bg);
        color: var(--sn-nav-text);
        text-decoration: none;
        font-size: 0.9rem;
      }
      .tabs a.active {
        background: var(--sn-nav-active-bg);
        color: var(--sn-nav-active-text);
      }
      .shell {
        min-height: 12rem;
      }
    `,
  ],
})
export class WorkLayoutComponent {}
