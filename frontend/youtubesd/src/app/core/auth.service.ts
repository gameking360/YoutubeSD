import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { CONFIG } from './config';

interface TokenPair { accessToken: string; refreshToken: string; expiresIn: number; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private _token = signal<string | null>(localStorage.getItem('accessToken'));
  private refreshing: Promise<boolean> | null = null;
  private timer?: ReturnType<typeof setTimeout>;

  readonly token = this._token.asReadonly();
  readonly logged = computed(() => !!this._token());

  constructor() { this.agendarRenovacao(); }

  private claims(): any {
    const t = this._token();
    if (!t) return {};
    try { return JSON.parse(atob(t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))); } catch { return {}; }
  }

  async login(email: string, password: string) {
    this.guardar(await firstValueFrom(this.http.post<TokenPair>(CONFIG.gateway + CONFIG.paths.login, { email, password })));
  }

  async register(name: string, email: string, password: string) {
    await firstValueFrom(this.http.post(CONFIG.gateway + CONFIG.paths.register, { name, email, password }));
  }

  // O refresh token é de uso único e renovações concorrentes revogam a sessão:
  // por isso só existe uma renovação em andamento por vez.
  refresh(): Promise<boolean> {
    if (this.refreshing) return this.refreshing;
    const rt = localStorage.getItem('refreshToken');
    if (!rt) return Promise.resolve(false);
    this.refreshing = firstValueFrom(this.http.post<TokenPair>(CONFIG.gateway + CONFIG.paths.refresh, { refreshToken: rt }))
      .then((r) => { this.guardar(r); return true; })
      .catch(() => { this.logout(); return false; })
      .finally(() => { this.refreshing = null; });
    return this.refreshing;
  }

  logout() {
    clearTimeout(this.timer);
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    this._token.set(null);
  }

  private guardar(r: TokenPair) {
    localStorage.setItem('accessToken', r.accessToken);
    localStorage.setItem('refreshToken', r.refreshToken);
    this._token.set(r.accessToken);
    this.agendarRenovacao();
  }

  // renova 1 min antes de expirar, para o hls.js (que usa o token nos segmentos) nunca ficar sem token válido
  private agendarRenovacao() {
    clearTimeout(this.timer);
    const exp = this.claims().exp;
    if (!exp) return;
    const ms = Math.max(exp * 1000 - Date.now() - 60_000, 0);
    this.timer = setTimeout(() => this.refresh(), ms);
  }
}
