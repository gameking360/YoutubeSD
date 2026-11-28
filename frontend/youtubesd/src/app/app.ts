import { Component, effect, inject } from '@angular/core';
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
          <a routerLink="/perfil" routerLinkActive="ativo">Perfil</a>
          @if (api.usuario(); as u) { <span class="suave">Olá, {{ u.name }}</span> }
          <button class="sec" (click)="sair()">Sair</button>
        </nav>
      }
    </header>
    <main><router-outlet /></main>
  `,
})
export class App {
  auth = inject(AuthService);
  api = inject(ApiService);
  private router = inject(Router);

  constructor() {
    effect(() => {
      if (this.auth.logged()) this.api.me().subscribe({ error: () => {} });
      else this.api.usuario.set(null);
    });
  }

  sair() {
    try { sessionStorage.removeItem('ultimoVideo'); } catch { /* sem storage */ }
    // sai da página primeiro: o player grava a posição atual (ainda com o token) antes do logout
    this.router.navigateByUrl('/login').finally(() => this.auth.logout());
  }
}
