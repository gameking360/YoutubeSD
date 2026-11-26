import { Component, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiService } from './core/api.service';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topo">
      <h1>YoutubeSD</h1>
      @if (auth.logged()) {
        <nav>
          <a routerLink="/watch" routerLinkActive="ativo">Assistir</a>
          <a routerLink="/upload" routerLinkActive="ativo">Enviar vídeo</a>
          @if (nome()) { <span class="suave">Olá, {{ nome() }}</span> }
          <button class="sec" (click)="sair()">Sair</button>
        </nav>
      }
    </header>
    <main><router-outlet /></main>
  `,
})
export class App {
  auth = inject(AuthService);
  private api = inject(ApiService);
  private router = inject(Router);
  nome = signal('');

  constructor() {
    effect(() => {
      if (this.auth.logged()) this.api.me().subscribe({ next: (u) => this.nome.set(u.name), error: () => {} });
      else this.nome.set('');
    });
  }

  sair() { this.auth.logout(); this.router.navigateByUrl('/login'); }
}
