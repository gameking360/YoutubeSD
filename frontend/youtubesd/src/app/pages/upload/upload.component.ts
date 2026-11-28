import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  ApiService, Notification, Quota, UploadDetail, UploadStatus, Visibility,
} from '../../core/api.service';
import { CONFIG } from '../../core/config';

const VISIBILIDADES: { v: Visibility; nome: string; ajuda: string }[] = [
  { v: 'PUBLIC', nome: 'Público', ajuda: 'aparece no catálogo' },
  { v: 'UNLISTED', nome: 'Não listado', ajuda: 'só quem tiver o link' },
  { v: 'PRIVATE', nome: 'Privado', ajuda: 'só você' },
];

@Component({
  selector: 'app-upload',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="grid">
      <section>
        <div class="card">
          <h2>Enviar vídeo</h2>
          @if (cota(); as c) {
            <p class="suave">Cota de uploads: {{ c.used }} usados · {{ c.reserved }} em andamento · {{ c.available }} disponíveis (limite {{ c.limit }})</p>
          }
          <form (ngSubmit)="enviar()">
            <label>Título <input name="titulo" [(ngModel)]="titulo" maxlength="255" required [disabled]="enviando()"></label>
            <label>Visibilidade
              <select name="visibilidade" [(ngModel)]="visibilidade" [disabled]="enviando()">
                @for (o of visibilidades; track o.v) { <option [value]="o.v">{{ o.nome }} — {{ o.ajuda }}</option> }
              </select>
            </label>
            <label>Arquivo <input type="file" accept="video/*,.mkv,.avi,.mov,.webm" (change)="escolher($event)" [disabled]="enviando()"></label>
            <button type="submit" [disabled]="enviando() || !arquivo || (cota() !== null && cota()!.available <= 0)">
              {{ enviando() ? 'Enviando…' : 'Enviar' }}
            </button>
          </form>
          @if (enviando()) {
            <p class="suave">{{ etapa() }} — {{ partesOk() }} de {{ totalPartes() }} partes ({{ progresso() }}%)</p>
            <progress [value]="progresso()" max="100" style="width:100%"></progress>
          }
          @if (replicas().length) {
            <p class="suave">Partes por réplica do Upload:
              @for (r of replicas(); track r[0]) { <span class="badge">{{ r[0] }}: {{ r[1] }}</span> }
            </p>
          }
          @if (erro()) { <p class="erro">{{ erro() }}</p> }
          @if (ok()) { <p class="ok">{{ ok() }}</p> }
        </div>

        <div class="card">
          <div class="cab"><h2>Meus uploads</h2><button class="sec" (click)="carregar()">Atualizar</button></div>
          <p class="suave">Fluxo: PENDING → PROCESSING → PUBLISHED. Se a cota for rejeitada ou o processamento falhar, o vídeo vira FAILED e a vaga é devolvida.</p>
          <ul class="lista uploads">
            @for (u of uploads(); track u.videoId) {
              <li class="up">
                <div class="linha">
                  <span>
                    {{ u.title || u.videoId }}
                    <small>
                      {{ nomeVis(u.visibility) }}
                      @if (u.sizeBytes) { · {{ tamanho(u.sizeBytes) }} }
                      @if (u.durationSec) { · {{ fmt(u.durationSec) }} }
                      @if (u.createdAt) { · enviado em {{ data(u.createdAt) }} }
                    </small>
                  </span>
                  <span>
                    <span class="badge" [class]="u.status">{{ u.status }}</span>
                  </span>
                </div>
                @if (u.failureReason) { <p class="erro">Motivo: {{ u.failureReason }}</p> }
                <div class="acoes">
                  @if (u.status === 'PUBLISHED') {
                    <a class="btn sec" routerLink="/watch" [queryParams]="{ v: u.videoId }">Assistir</a>
                    <button class="sec" (click)="copiarLink(u.videoId)">{{ copiado() === u.videoId ? 'Link copiado' : 'Copiar link' }}</button>
                  }
                  @if (u.status === 'PENDING' && !enviando()) {
                    <label class="btn sec">Retomar envio
                      <input type="file" hidden (change)="retomar(u, $event)">
                    </label>
                  }
                  <button class="sec" (click)="alternarDetalhe(u.videoId)">{{ aberto() === u.videoId ? 'Ocultar detalhes' : 'Detalhes' }}</button>
                  @if (u.status === 'PENDING' || u.status === 'PROCESSING' || u.status === 'PUBLISHED') {
                    <button class="sec" (click)="remover(u)">{{ u.status === 'PUBLISHED' ? 'Remover' : 'Cancelar' }}</button>
                  }
                </div>
                @if (aberto() === u.videoId) {
                  @if (detalhe(); as d) {
                    <dl class="detalhe">
                      <dt>ID</dt><dd><code>{{ d.videoId }}</code></dd>
                      <dt>Arquivo</dt><dd>{{ d.fileName }} ({{ tamanho(d.sizeBytes ?? 0) }})</dd>
                      <dt>Envio</dt><dd>{{ d.uploadState }} · {{ d.receivedParts.length }}/{{ d.totalParts }} partes · {{ tamanho(d.uploadedBytes) }}</dd>
                      <dt>Cota</dt><dd>{{ d.quotaState }}</dd>
                      <dt>Processamento</dt><dd>{{ d.processingState || '—' }} · {{ d.processingAttempts }} tentativa(s)</dd>
                      @if (d.queueDeadlineAt) { <dt>Prazo na fila</dt><dd>{{ data(d.queueDeadlineAt) }}</dd> }
                      @if (d.deadlineAt) { <dt>Prazo de processamento</dt><dd>{{ data(d.deadlineAt) }}</dd> }
                      @if (d.renditions?.length) {
                        <dt>Resoluções</dt><dd>@for (r of d.renditions; track r.name) { <span class="badge">{{ r.name }} · {{ r.width }}×{{ r.height }} · {{ kbps(r.bandwidth) }}</span> }</dd>
                      }
                      @if (d.publishedAt) { <dt>Publicado em</dt><dd>{{ data(d.publishedAt) }}</dd> }
                      <dt>Atualizado em</dt><dd>{{ data(d.updatedAt) }}</dd>
                    </dl>
                  } @else { <p class="suave">Carregando detalhes…</p> }
                }
              </li>
            } @empty { <li class="suave">Nenhum upload ainda.</li> }
          </ul>
          @if (total() > tamanhoPagina) {
            <div class="paginacao">
              <button class="sec" [disabled]="pagina() === 0" (click)="irPara(pagina() - 1)">Anterior</button>
              <span class="suave">Página {{ pagina() + 1 }} de {{ paginas() }} · {{ total() }} vídeos</span>
              <button class="sec" [disabled]="pagina() + 1 >= paginas()" (click)="irPara(pagina() + 1)">Próxima</button>
            </div>
          }
        </div>
      </section>

      <aside>
        <div class="card">
          <div class="cab"><h2>Notificações</h2><button class="sec" (click)="carregarNotificacoes()">Atualizar</button></div>
          <ul class="lista notif">
            @for (n of notificacoes(); track n.id) {
              <li><span>
                <span class="badge" [class]="n.type">{{ tipoNotif(n.type) }}</span> {{ n.message }}
                <small>{{ tituloDe(n.videoId) }} · {{ data(n.createdAt) }}</small>
              </span></li>
            } @empty { <li class="suave">Nenhuma notificação.</li> }
          </ul>
        </div>
      </aside>
    </div>
  `,
})
export class UploadComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  readonly visibilidades = VISIBILIDADES;
  readonly tamanhoPagina = 10;

  titulo = '';
  visibilidade: Visibility = 'PUBLIC';
  arquivo: File | null = null;
  private input: HTMLInputElement | null = null;

  enviando = signal(false);
  etapa = signal('');
  progresso = signal(0);
  partesOk = signal(0);
  totalPartes = signal(0);
  replicas = signal<[string, number][]>([]);
  erro = signal('');
  ok = signal('');
  uploads = signal<UploadStatus[]>([]);
  pagina = signal(0);
  total = signal(0);
  cota = signal<Quota | null>(null);
  notificacoes = signal<Notification[]>([]);
  aberto = signal<string | null>(null);
  detalhe = signal<UploadDetail | null>(null);
  copiado = signal<string | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit() {
    this.carregar();
    this.carregarCota();
    this.carregarNotificacoes();
    // acompanha o status enquanto houver vídeo em andamento
    this.timer = setInterval(() => {
      if (this.uploads().some((u) => u.status === 'PENDING' || u.status === 'PROCESSING')) {
        this.carregar();
        this.carregarCota();
        this.carregarNotificacoes();
      }
    }, CONFIG.uploadPollMs);
  }
  ngOnDestroy() { clearInterval(this.timer); }

  // ---- formatação ----
  paginas() { return Math.max(1, Math.ceil(this.total() / this.tamanhoPagina)); }
  nomeVis(v?: Visibility) { return VISIBILIDADES.find((o) => o.v === v)?.nome ?? v ?? ''; }
  tituloDe(id: string) { return this.uploads().find((u) => u.videoId === id)?.title ?? id; }
  tipoNotif(t: string) {
    return ({ VIDEO_PUBLISHED: 'PUBLICADO', VIDEO_FAILED: 'FALHOU', VIDEO_REMOVED: 'REMOVIDO' } as Record<string, string>)[t] ?? t;
  }
  // o .NET serializa as datas do MySQL sem fuso: são UTC
  data(iso: string) { return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : iso + 'Z').toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }); }
  fmt(seg: number) { const s = Math.floor(seg); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; }
  kbps(b: number) { return `${Math.round(b / 1000)} kbps`; }
  tamanho(b: number) {
    if (b >= 1 << 30) return `${(b / (1 << 30)).toFixed(2)} GiB`;
    if (b >= 1 << 20) return `${(b / (1 << 20)).toFixed(1)} MiB`;
    return `${Math.ceil(b / 1024)} KiB`;
  }

  // ---- carregamento ----
  escolher(e: Event) {
    this.input = e.target as HTMLInputElement;
    this.arquivo = this.input.files?.[0] ?? null;
    if (this.arquivo && !this.titulo) this.titulo = this.arquivo.name.replace(/\.[^.]+$/, '');
  }

  carregarCota() { this.api.quota().subscribe({ next: (c) => this.cota.set(c), error: () => {} }); }
  carregarNotificacoes() { this.api.notifications().subscribe({ next: (n) => this.notificacoes.set(n), error: () => {} }); }

  async carregar() {
    try {
      const r = await firstValueFrom(this.api.myUploads(this.pagina(), this.tamanhoPagina));
      this.uploads.set(r.items ?? []);
      this.total.set(r.total ?? r.items.length);
    } catch { /* mantém a lista */ }
    if (this.aberto()) this.carregarDetalhe(this.aberto()!);
  }

  irPara(p: number) { this.pagina.set(p); this.aberto.set(null); this.carregar(); }

  alternarDetalhe(id: string) {
    if (this.aberto() === id) { this.aberto.set(null); return; }
    this.aberto.set(id);
    this.detalhe.set(null);
    this.carregarDetalhe(id);
  }

  private async carregarDetalhe(id: string) {
    try {
      const d = await firstValueFrom(this.api.uploadDetail(id));
      if (this.aberto() === id) this.detalhe.set(d);
    } catch { /* tenta no próximo ciclo */ }
  }

  // ---- envio ----
  async enviar() {
    const file = this.arquivo;
    if (!file) return;
    this.iniciarEnvio('Criando upload');
    let videoId = '';
    try {
      const init = await firstValueFrom(this.api.createUpload(this.titulo, file, this.visibilidade));
      videoId = init.videoId;
      await this.carregar();
      await this.enviarPartes(videoId, file, init.chunkSize, init.totalParts, intervalo(1, init.totalParts), 0);
      await this.concluir(videoId);
      this.titulo = '';
      this.arquivo = null;
      if (this.input) this.input.value = '';
    } catch (e: any) {
      this.erro.set(this.mensagem(e) + (videoId && e?.status !== 409 ? ' — o envio ficou salvo: use "Retomar envio" escolhendo o mesmo arquivo.' : ''));
    } finally {
      this.finalizarEnvio();
    }
  }

  // Retoma um envio interrompido (aba fechada, rede caiu…): o Upload informa as partes que já recebeu.
  async retomar(u: UploadStatus, e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.iniciarEnvio('Consultando partes recebidas');
    try {
      const d = await firstValueFrom(this.api.uploadDetail(u.videoId));
      if (d.status !== 'PENDING') throw { mensagem: `O vídeo está ${d.status} e não aceita mais partes.` };
      if (file.size !== d.sizeBytes) {
        throw { mensagem: `Arquivo diferente: o upload espera "${d.fileName}" com ${d.sizeBytes} bytes (escolhido: ${file.size}).` };
      }
      if (d.uploadState === 'RECEIVING') {
        const recebidas = new Set(d.receivedParts);
        const faltando = intervalo(1, d.totalParts).filter((n) => !recebidas.has(n));
        await this.enviarPartes(u.videoId, file, d.chunkSize, d.totalParts, faltando, d.uploadedBytes);
      }
      await this.concluir(u.videoId);
    } catch (err: any) {
      this.erro.set(err?.mensagem ?? this.mensagem(err));
    } finally {
      this.finalizarEnvio();
    }
  }

  private iniciarEnvio(etapa: string) {
    this.erro.set('');
    this.ok.set('');
    this.enviando.set(true);
    this.etapa.set(etapa);
    this.progresso.set(0);
    this.partesOk.set(0);
    this.totalPartes.set(0);
    this.replicas.set([]);
  }

  private async finalizarEnvio() {
    this.enviando.set(false);
    await this.carregar();
    this.carregarCota();
    this.carregarNotificacoes();
  }

  private async concluir(videoId: string) {
    this.etapa.set('Concluindo');
    await firstValueFrom(this.api.completeUpload(videoId));
    this.progresso.set(100);
    this.ok.set('Envio concluído. O vídeo segue para processamento em segundo plano.');
  }

  // Envia as partes em paralelo (cada uma pode cair em uma réplica diferente do Upload).
  private async enviarPartes(videoId: string, file: File, chunk: number, total: number, partes: number[], jaEnviados: number) {
    this.etapa.set('Enviando partes');
    this.totalPartes.set(total);
    this.partesOk.set(total - partes.length);
    let enviados = jaEnviados;
    this.progresso.set(Math.round((enviados * 100) / file.size));
    const fila = [...partes];
    const porReplica = new Map<string, number>();
    let falha: unknown = null;

    const trabalhador = async () => {
      while (fila.length && !falha) {
        const n = fila.shift()!;
        const blob = file.slice((n - 1) * chunk, Math.min(n * chunk, file.size));
        try {
          const replica = await this.enviarParte(videoId, n, blob);
          porReplica.set(replica, (porReplica.get(replica) ?? 0) + 1);
          this.replicas.set([...porReplica.entries()]);
        } catch (e) { falha = e; return; }
        enviados += blob.size;
        this.partesOk.update((x) => x + 1);
        this.progresso.set(Math.round((enviados * 100) / file.size));
      }
    };
    await Promise.all(Array.from({ length: Math.min(CONFIG.uploadConcurrency, fila.length) }, trabalhador));
    if (falha) throw falha;
  }

  // Repete a parte em falhas transitórias (reenviar só sobrescreve; 503 traz Retry-After quando a réplica está cheia).
  private async enviarParte(videoId: string, n: number, blob: Blob): Promise<string> {
    for (let tentativa = 1; ; tentativa++) {
      try {
        const r = await firstValueFrom(this.api.uploadPart(videoId, n, blob));
        return r.headers.get('X-Instance-Id') ?? '?';
      } catch (e: any) {
        const s = e?.status ?? 0;
        const transitorio = s === 0 || s === 408 || s === 429 || s >= 500;
        if (!transitorio || tentativa >= 6) throw e;
        const retryAfter = Number(e?.headers?.get?.('Retry-After'));
        await new Promise((r) => setTimeout(r, (retryAfter > 0 ? retryAfter : tentativa) * 1000));
      }
    }
  }

  async remover(u: UploadStatus) {
    const acao = u.status === 'PUBLISHED' ? 'Remover' : 'Cancelar';
    if (!confirm(`${acao} "${u.title || u.videoId}"?`)) return;
    this.erro.set('');
    try { await firstValueFrom(this.api.removeUpload(u.videoId)); } catch (e: any) { this.erro.set(this.mensagem(e)); }
    await this.carregar();
    this.carregarCota();
    this.carregarNotificacoes();
  }

  async copiarLink(id: string) {
    const link = `${location.origin}/watch?v=${encodeURIComponent(id)}`;
    try { await navigator.clipboard.writeText(link); } catch { prompt('Link do vídeo:', link); return; }
    this.copiado.set(id);
    setTimeout(() => this.copiado.set(null), 2000);
  }

  private mensagem(e: any): string {
    const err = e?.error;
    const campos = err?.errors ? Object.values(err.errors).flat().join(' ') : '';
    const detalhe = campos || err?.detail || err?.title || err?.message;
    return `Falha (${e?.status ?? 'sem resposta'})${detalhe ? ': ' + detalhe : ''}`;
  }
}

function intervalo(de: number, ate: number) { return Array.from({ length: Math.max(0, ate - de + 1) }, (_, i) => de + i); }
