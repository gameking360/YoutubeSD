import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, firstValueFrom } from 'rxjs';
import Hls from 'hls.js';
import { ApiService, Playback, Progress, VideoItem } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { CONFIG } from '../../core/config';

/** Vídeo que está no player agora. */
interface Atual {
  videoId: string;
  durationSec: number;
  resumeSec: number;
  /** Só depois que o player chegou ao ponto de retomada a posição dele vale para salvar. */
  pronto: boolean;
}

/** Posição lida de forma síncrona, antes de qualquer troca de vídeo ou destruição do player. */
interface Snapshot { videoId: string; positionSec: number; durationSec: number; clientTs: string; }

const ULTIMO_VIDEO = 'ultimoVideo';

@Component({
  selector: 'app-watch',
  template: `
    <div class="grid">
      <section>
        <div class="player">
          <video #video controls playsinline></video>
          @if (!sessao() && !carregando()) { <p class="vazio">Escolha um vídeo do catálogo.</p> }
          @if (carregando()) { <p class="vazio">Carregando…</p> }
        </div>
        @if (erro()) { <p class="erro card">{{ erro() }}</p> }
        @if (sessao(); as s) {
          <div class="card">
            <div class="cab">
              <h2>{{ s.title }}</h2>
              <button class="sec" (click)="copiarLink(s.videoId)">{{ copiado() ? 'Link copiado' : 'Copiar link' }}</button>
            </div>
            <div class="qual">
              <span class="suave">Qualidade:</span>
              <button class="sec" [class.ativo]="nivel() === -1" (click)="escolherNivel(-1)">Automática</button>
              @for (n of niveis(); track n.i) {
                <button class="sec" [class.ativo]="nivel() === n.i" (click)="escolherNivel(n.i)">{{ n.nome }}</button>
              }
              @if (tocando()) { <span class="suave">tocando em {{ tocando() }}</span> }
            </div>
            <p class="suave">
              @if (s.resumePositionSec > 0) { Retomado do ponto salvo: <b>{{ fmt(s.resumePositionSec) }}</b> }
              @else { Começou do início }
              · duração {{ fmt(s.durationSec) }}
              · último progresso salvo: {{ salvo() }}
            </p>
            @if (aviso()) { <p class="suave">{{ aviso() }}</p> }
          </div>
        }
      </section>

      <aside>
        <div class="card">
          <div class="cab"><h2>Catálogo</h2><button class="sec" (click)="carregarCatalogo()">Atualizar</button></div>
          <ul class="lista">
            @for (v of catalogo(); track v.videoId) {
              <li><button (click)="tocar(v.videoId)" [class.ativo]="sessao()?.videoId === v.videoId">
                {{ v.title }}<small>{{ fmt(v.durationSec) }}@if (v.publishedAt) { · publicado em {{ data(v.publishedAt) }} }</small></button></li>
            } @empty { <li class="suave">Nenhum vídeo publicado.</li> }
          </ul>
          @if (temMais()) { <button class="sec mais" (click)="carregarCatalogo(true)">Carregar mais</button> }
        </div>
        <div class="card">
          <div class="cab"><h2>Continuar assistindo</h2><button class="sec" (click)="carregarProgresso()">Atualizar</button></div>
          <ul class="lista">
            @for (p of continuar(); track p.videoId) {
              <li><button (click)="tocar(p.videoId)" [class.ativo]="sessao()?.videoId === p.videoId">{{ titulo(p.videoId) }}
                <small>parou em {{ fmt(p.positionSec) }} de {{ fmt(p.durationSec) }}</small>
                <span class="barra"><span [style.width.%]="pct(p)"></span></span></button></li>
            } @empty { <li class="suave">Nada por aqui.</li> }
          </ul>
        </div>
        <div class="card">
          <div class="cab"><h2>Concluídos</h2></div>
          <ul class="lista">
            @for (p of concluidos(); track p.videoId) {
              <li><button (click)="tocar(p.videoId)" [class.ativo]="sessao()?.videoId === p.videoId">{{ titulo(p.videoId) }}
                <small>assistido@if (p.lastWatchedAt) { em {{ data(p.lastWatchedAt) }} } · assistir de novo</small></button></li>
            } @empty { <li class="suave">Nenhum vídeo concluído.</li> }
          </ul>
        </div>
      </aside>
    </div>
  `,
})
export class WatchComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  private auth = inject(AuthService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  @ViewChild('video', { static: true }) videoRef!: ElementRef<HTMLVideoElement>;

  catalogo = signal<VideoItem[]>([]);
  temMais = signal(false);
  continuar = signal<Progress[]>([]);
  concluidos = signal<Progress[]>([]);
  sessao = signal<Playback | null>(null);
  niveis = signal<{ i: number; nome: string }[]>([]);
  nivel = signal(-1);
  tocando = signal('');
  salvo = signal('—');
  carregando = signal(false);
  erro = signal('');
  aviso = signal('');
  copiado = signal(false);
  private titulos = signal<Record<string, string>>({});

  private paginaCatalogo = 0;
  private hls: Hls | null = null;
  private atual: Atual | null = null;
  private seq = 0;
  private timer?: ReturnType<typeof setInterval>;
  private seekTimer?: ReturnType<typeof setTimeout>;
  private sub?: Subscription;
  private get v() { return this.videoRef.nativeElement; }

  // ---- eventos do player ----
  private onPlay = () => { this.aviso.set(''); this.iniciarTimer(); };
  private onPlaying = () => { if (this.atual) this.atual.pronto = true; };
  private onCanPlay = () => {
    // com startPosition o hls.js já posicionou o vídeo no ponto de retomada
    const a = this.atual;
    if (a && (a.resumeSec === 0 || this.v.currentTime >= a.resumeSec - 3)) a.pronto = true;
  };
  private onPause = () => { this.pararTimer(); if (!this.v.ended) this.salvarAgora(); };
  private onSeeked = () => {
    // arrastar a barra gera vários seeks: salva só o último
    clearTimeout(this.seekTimer);
    this.seekTimer = setTimeout(() => this.salvarAgora(), 700);
  };
  private onEnded = () => { this.pararTimer(); this.salvarAgora().then(() => this.carregarProgresso()); };
  // trocar de aba do navegador, minimizar, recarregar ou fechar
  private onHidden = () => { if (document.visibilityState === 'hidden') this.salvarAoSair(); };
  private onPageHide = () => this.salvarAoSair();

  ngOnInit() {
    this.carregarCatalogo();
    this.carregarProgresso();
    this.carregarTitulosDosMeusUploads();

    const v = this.v;
    v.addEventListener('play', this.onPlay);
    v.addEventListener('playing', this.onPlaying);
    v.addEventListener('canplay', this.onCanPlay);
    v.addEventListener('pause', this.onPause);
    v.addEventListener('seeked', this.onSeeked);
    v.addEventListener('ended', this.onEnded);
    document.addEventListener('visibilitychange', this.onHidden);
    window.addEventListener('pagehide', this.onPageHide);

    // O vídeo aberto fica na URL (?v=), então recarregar a página ou voltar para esta aba reabre o mesmo
    // vídeo, e o serviço de download devolve o ponto em que parou (resumePositionSec).
    this.sub = this.route.queryParamMap.subscribe((q) => {
      const id = q.get('v');
      if (id) {
        if (id !== this.atual?.videoId) this.abrir(id);
        return;
      }
      const ultimo = lerStorage(ULTIMO_VIDEO);
      if (ultimo) this.router.navigate([], { relativeTo: this.route, queryParams: { v: ultimo }, replaceUrl: true });
    });
  }

  ngOnDestroy() {
    // saiu da página "Assistir" pelo menu: grava a posição antes de destruir o player
    const snap = this.snapshot();
    if (snap) this.salvar(snap);

    const v = this.v;
    v.removeEventListener('play', this.onPlay);
    v.removeEventListener('playing', this.onPlaying);
    v.removeEventListener('canplay', this.onCanPlay);
    v.removeEventListener('pause', this.onPause);
    v.removeEventListener('seeked', this.onSeeked);
    v.removeEventListener('ended', this.onEnded);
    document.removeEventListener('visibilitychange', this.onHidden);
    window.removeEventListener('pagehide', this.onPageHide);
    this.sub?.unsubscribe();
    clearTimeout(this.seekTimer);
    this.encerrarPlayer();
  }

  // ---- formatação ----
  fmt(seg: number) {
    const s = Math.max(0, Math.floor(seg || 0));
    const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = String(s % 60).padStart(2, '0');
    return h ? `${h}:${String(m).padStart(2, '0')}:${r}` : `${m}:${r}`;
  }
  data(iso: string) { return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }); }
  pct(p: Progress) { return p.durationSec ? Math.min(100, (p.positionSec * 100) / p.durationSec) : 0; }
  titulo(id: string) { return this.titulos()[id] ?? id; }

  // ---- listas ----
  async carregarCatalogo(mais = false) {
    this.paginaCatalogo = mais ? this.paginaCatalogo + 1 : 0;
    try {
      const r = await firstValueFrom(this.api.catalog(this.paginaCatalogo, 20));
      const itens = r.items ?? [];
      this.catalogo.set(mais ? [...this.catalogo(), ...itens] : itens);
      this.temMais.set(!!r.hasNext);
      this.lembrarTitulos(itens.map((v) => [v.videoId, v.title]));
    } catch { if (!mais) this.catalogo.set([]); }
  }

  async carregarProgresso() {
    const [c, f] = await Promise.allSettled([
      firstValueFrom(this.api.progressList('IN_PROGRESS')),
      firstValueFrom(this.api.progressList('COMPLETED')),
    ]);
    this.continuar.set(c.status === 'fulfilled' ? c.value : []);
    this.concluidos.set(f.status === 'fulfilled' ? f.value : []);
  }

  // vídeos UNLISTED/PRIVATE do próprio usuário não aparecem no catálogo público
  private async carregarTitulosDosMeusUploads() {
    try {
      const r = await firstValueFrom(this.api.myUploads(0, 100));
      this.lembrarTitulos(r.items.map((u) => [u.videoId, u.title ?? u.videoId]));
    } catch { /* só melhora os nomes nas listas */ }
  }

  private lembrarTitulos(pares: [string, string][]) {
    if (pares.length) this.titulos.update((t) => ({ ...t, ...Object.fromEntries(pares) }));
  }

  // ---- reprodução ----
  tocar(id: string) {
    // mesmo vídeo (ou já está na URL): reabre direto, do ponto salvo
    if (id === this.atual?.videoId || id === this.route.snapshot.queryParamMap.get('v')) { this.abrir(id); return; }
    this.router.navigate([], { relativeTo: this.route, queryParams: { v: id } });
  }

  private async abrir(id: string) {
    const seq = ++this.seq;

    // 1) Lê a posição do vídeo atual ANTES de mexer no player e encerra o player.
    //    Assim nenhum evento do vídeo antigo (pause, seeked, timer) grava posição no vídeo novo.
    const snap = this.snapshot();
    this.encerrarPlayer();
    this.erro.set('');
    this.aviso.set('');
    this.carregando.set(true);

    // 2) Garante que o ponto do vídeo anterior chegou ao serviço de download.
    if (snap) await this.salvar(snap);
    if (seq !== this.seq) return;

    // 3) Abre a sessão: o download devolve resumePositionSec (Redis → MySQL).
    let s: Playback;
    try {
      s = await firstValueFrom(this.api.playback(id));
    } catch (e: any) {
      if (seq !== this.seq) return;
      this.carregando.set(false);
      this.sessao.set(null);
      this.erro.set(e?.status === 404 ? 'Vídeo não encontrado ou sem permissão para assistir.' : `Falha ao abrir o vídeo (${e?.status ?? 'sem resposta'}).`);
      removerStorage(ULTIMO_VIDEO);
      return;
    }
    if (seq !== this.seq) return;

    gravarStorage(ULTIMO_VIDEO, s.videoId);
    this.lembrarTitulos([[s.videoId, s.title]]);
    this.sessao.set(s);
    this.salvo.set('—');
    this.niveis.set([]);
    this.nivel.set(-1);
    this.tocando.set('');
    this.atual = { videoId: s.videoId, durationSec: s.durationSec, resumeSec: s.resumePositionSec, pronto: false };
    this.iniciarPlayer(s);
    this.carregando.set(false);
    if (snap) this.carregarProgresso();
  }

  private iniciarPlayer(s: Playback) {
    const url = CONFIG.gateway + s.manifestUrl;
    const v = this.v;

    if (Hls.isSupported()) {
      let tentativas = 0;
      const hls = new Hls({
        startPosition: s.resumePositionSec,
        // o back exige o JWT também nos manifestos e segmentos
        xhrSetup: (xhr) => { const t = this.auth.token(); if (t) xhr.setRequestHeader('Authorization', `Bearer ${t}`); },
      });
      this.hls = hls;
      hls.on(Hls.Events.MANIFEST_PARSED, (_, d) =>
        this.niveis.set(d.levels.map((l, i) => ({ i, nome: `${l.height}p` }))));
      hls.on(Hls.Events.LEVEL_SWITCHED, (_, d) => {
        const l = hls.levels[d.level];
        this.tocando.set(l ? `${l.height}p` : '');
      });
      hls.on(Hls.Events.ERROR, async (_, d) => {
        if (!d.fatal || this.hls !== hls) return;
        if (++tentativas > 5) { this.erro.set('Não foi possível carregar o vídeo.'); return; }
        if (d.type === Hls.ErrorTypes.NETWORK_ERROR) {
          // token venceu no meio do vídeo ou uma réplica do download caiu: renova e tenta de novo
          if ((d.response as any)?.code === 401) await this.auth.refresh();
          setTimeout(() => { if (this.hls === hls) hls.startLoad(); }, 1000);
        } else if (d.type === Hls.ErrorTypes.MEDIA_ERROR) {
          hls.recoverMediaError();
        } else {
          this.erro.set('Erro no player de vídeo.');
        }
      });
      hls.loadSource(url);
      hls.attachMedia(v);
    } else if (v.canPlayType('application/vnd.apple.mpegurl')) {
      // Safari (HLS nativo): posiciona manualmente
      v.src = url;
      v.addEventListener('loadedmetadata', () => { v.currentTime = s.resumePositionSec; }, { once: true });
    } else {
      this.erro.set('Este navegador não suporta HLS.');
      return;
    }

    v.play().catch(() => {
      // o navegador bloqueia autoplay com som depois de recarregar a página
      if (s.resumePositionSec > 0) this.aviso.set(`Clique em ▶ para continuar de ${this.fmt(s.resumePositionSec)}.`);
    });
  }

  private encerrarPlayer() {
    this.pararTimer();
    this.atual = null;
    if (this.hls) { this.hls.destroy(); this.hls = null; }
    const v = this.v;
    if (v.getAttribute('src')) { v.removeAttribute('src'); v.load(); }
  }

  escolherNivel(i: number) {
    if (!this.hls) return;
    this.hls.currentLevel = i;
    this.nivel.set(i);
  }

  async copiarLink(id: string) {
    const link = `${location.origin}/watch?v=${encodeURIComponent(id)}`;
    try { await navigator.clipboard.writeText(link); } catch { prompt('Link do vídeo:', link); return; }
    this.copiado.set(true);
    setTimeout(() => this.copiado.set(false), 2000);
  }

  // ---- progresso ----
  private snapshot(): Snapshot | null {
    const a = this.atual;
    if (!a || !a.pronto) return null;
    const v = this.v;
    const dur = a.durationSec > 0 ? a.durationSec : Math.floor(v.duration || 0);
    if (!dur) return null;
    const pos = v.ended ? dur : Math.min(Math.max(0, Math.floor(v.currentTime)), dur);
    return { videoId: a.videoId, positionSec: pos, durationSec: dur, clientTs: new Date().toISOString() };
  }

  private salvarAgora() {
    const snap = this.snapshot();
    return snap ? this.salvar(snap) : Promise.resolve();
  }

  private async salvar(s: Snapshot) {
    try {
      const r = await firstValueFrom(this.api.saveProgress(s.videoId, s.positionSec, s.durationSec, s.clientTs));
      if (this.atual?.videoId === s.videoId) {
        this.salvo.set(`${this.fmt(r.positionSec)} · ${r.status === 'COMPLETED' ? 'concluído' : 'em andamento'}`);
      }
    } catch { /* o próximo ciclo tenta de novo */ }
  }

  // Página sendo fechada/recarregada ou aba escondida: fetch com keepalive sobrevive ao descarregamento
  // (sendBeacon não envia o header Authorization).
  private salvarAoSair() {
    const s = this.snapshot();
    if (!s) return;
    const body = JSON.stringify({ positionSec: s.positionSec, durationSec: s.durationSec, clientTs: s.clientTs });
    fetch(this.api.progressUrl(s.videoId), {
      method: 'PUT', keepalive: true,
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${this.auth.token()}` },
      body,
    }).then((r) => {
      // token vencido e a página ainda está viva (só trocou de aba): renova pelo HttpClient
      if (r.status === 401) this.salvar(s);
    }).catch(() => {});
  }

  private iniciarTimer() { this.pararTimer(); this.timer = setInterval(() => this.salvarAgora(), CONFIG.progressIntervalMs); }
  private pararTimer() { clearInterval(this.timer); this.timer = undefined; }
}

function lerStorage(k: string) { try { return sessionStorage.getItem(k); } catch { return null; } }
function gravarStorage(k: string, v: string) { try { sessionStorage.setItem(k, v); } catch { /* sem storage */ } }
function removerStorage(k: string) { try { sessionStorage.removeItem(k); } catch { /* sem storage */ } }
