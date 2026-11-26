import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import Hls from 'hls.js';
import { ApiService, Playback, Progress, VideoItem } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { CONFIG } from '../../core/config';

@Component({
  selector: 'app-watch',
  template: `
    <div class="grid">
      <section>
        <div class="player">
          <video #video controls playsinline></video>
          @if (!sessao()) { <p class="vazio">Escolha um vídeo do catálogo.</p> }
        </div>
        @if (sessao(); as s) {
          <div class="card">
            <h2>{{ s.title }}</h2>
            <div class="qual">
              <span class="suave">Qualidade:</span>
              <button class="sec" [class.ativo]="nivel() === -1" (click)="escolherNivel(-1)">Automática</button>
              @for (n of niveis(); track n.i) {
                <button class="sec" [class.ativo]="nivel() === n.i" (click)="escolherNivel(n.i)">{{ n.nome }}</button>
              }
            </div>
            <p class="suave">Retomou em {{ fmt(s.resumePositionSec) }} · Último progresso salvo: {{ salvo() }}</p>
          </div>
        }
      </section>

      <aside>
        <div class="card">
          <div class="cab"><h2>Catálogo</h2><button class="sec" (click)="carregarCatalogo()">Atualizar</button></div>
          <ul class="lista">
            @for (v of catalogo(); track v.videoId) {
              <li><button (click)="tocar(v.videoId)" [class.ativo]="sessao()?.videoId === v.videoId">
                {{ v.title }}<small>{{ fmt(v.durationSec) }}</small></button></li>
            } @empty { <li class="suave">Nenhum vídeo publicado.</li> }
          </ul>
        </div>
        <div class="card">
          <div class="cab"><h2>Continuar assistindo</h2><button class="sec" (click)="carregarContinuar()">Atualizar</button></div>
          <ul class="lista">
            @for (p of continuar(); track p.videoId) {
              <li><button (click)="tocar(p.videoId)">{{ titulo(p.videoId) }}
                <small>parou em {{ fmt(p.positionSec) }} de {{ fmt(p.durationSec) }}</small></button></li>
            } @empty { <li class="suave">Nada por aqui.</li> }
          </ul>
        </div>
      </aside>
    </div>
  `,
})
export class WatchComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  private auth = inject(AuthService);
  @ViewChild('video', { static: true }) videoRef!: ElementRef<HTMLVideoElement>;

  catalogo = signal<VideoItem[]>([]);
  continuar = signal<Progress[]>([]);
  sessao = signal<Playback | null>(null);
  niveis = signal<{ i: number; nome: string }[]>([]);
  nivel = signal(-1);
  salvo = signal('—');

  private hls: Hls | null = null;
  private timer?: ReturnType<typeof setInterval>;
  private get v() { return this.videoRef.nativeElement; }

  private onPlay = () => this.iniciarTimer();
  private onPause = () => { this.pararTimer(); this.enviarProgresso(); };
  private onSeeked = () => this.enviarProgresso();
  private onEnded = () => { this.pararTimer(); this.enviarProgresso().then(() => this.carregarContinuar()); };
  private onHidden = () => { if (document.visibilityState === 'hidden') this.enviarProgresso(true); };

  ngOnInit() {
    this.carregarCatalogo();
    this.carregarContinuar();
    const v = this.v;
    v.addEventListener('play', this.onPlay);
    v.addEventListener('pause', this.onPause);
    v.addEventListener('seeked', this.onSeeked);
    v.addEventListener('ended', this.onEnded);
    document.addEventListener('visibilitychange', this.onHidden);
  }

  ngOnDestroy() {
    const v = this.v;
    v.removeEventListener('play', this.onPlay);
    v.removeEventListener('pause', this.onPause);
    v.removeEventListener('seeked', this.onSeeked);
    v.removeEventListener('ended', this.onEnded);
    document.removeEventListener('visibilitychange', this.onHidden);
    this.pararTimer();
    this.hls?.destroy();
  }

  fmt(seg: number) { const s = Math.max(0, Math.floor(seg || 0)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; }
  titulo(id: string) { return this.catalogo().find((v) => v.videoId === id)?.title ?? id; }

  async carregarCatalogo() {
    try { this.catalogo.set((await firstValueFrom(this.api.catalog())).items ?? []); } catch { this.catalogo.set([]); }
  }
  async carregarContinuar() {
    try { this.continuar.set(await firstValueFrom(this.api.continueWatching())); } catch { this.continuar.set([]); }
  }

  async tocar(id: string) {
    await this.enviarProgresso();
    this.pararTimer();
    let s: Playback;
    try { s = await firstValueFrom(this.api.playback(id)); } catch { return; }
    this.sessao.set(s);
    this.salvo.set('—');
    this.niveis.set([]);
    this.nivel.set(-1);

    this.hls?.destroy();
    this.hls = new Hls({
      startPosition: s.resumePositionSec,
      // o back exige o JWT também nos manifestos e segmentos
      xhrSetup: (xhr) => { const t = this.auth.token(); if (t) xhr.setRequestHeader('Authorization', `Bearer ${t}`); },
    });
    this.hls.on(Hls.Events.MANIFEST_PARSED, (_, d) =>
      this.niveis.set(d.levels.map((l, i) => ({ i, nome: `${l.height}p` }))));
    this.hls.loadSource(CONFIG.gateway + s.manifestUrl);
    this.hls.attachMedia(this.v);
    this.v.play().catch(() => {});
  }

  escolherNivel(i: number) {
    if (!this.hls) return;
    this.hls.currentLevel = i;
    this.nivel.set(i);
  }

  async enviarProgresso(aoFechar = false) {
    const s = this.sessao();
    if (!s || !this.v.currentSrc) return;
    const pos = Math.min(Math.floor(this.v.currentTime), s.durationSec);
    if (aoFechar) {
      // keepalive para sobreviver ao fechamento da aba (sendBeacon não envia o Authorization)
      fetch(`${CONFIG.gateway}${CONFIG.paths.progress}/${encodeURIComponent(s.videoId)}`, {
        method: 'PUT', keepalive: true,
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${this.auth.token()}` },
        body: JSON.stringify({ positionSec: pos, durationSec: s.durationSec, clientTs: new Date().toISOString() }),
      });
      return;
    }
    try {
      const r = await firstValueFrom(this.api.saveProgress(s.videoId, pos, s.durationSec));
      this.salvo.set(`${this.fmt(r.positionSec)} · ${r.status}`);
    } catch { /* o próximo ciclo tenta de novo */ }
  }

  private iniciarTimer() { this.pararTimer(); this.timer = setInterval(() => this.enviarProgresso(), CONFIG.progressIntervalMs); }
  private pararTimer() { clearInterval(this.timer); this.timer = undefined; }
}
