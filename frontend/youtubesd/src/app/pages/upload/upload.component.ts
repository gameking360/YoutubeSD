import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService, Quota, UploadStatus } from '../../core/api.service';
import { CONFIG } from '../../core/config';

@Component({
  selector: 'app-upload',
  imports: [FormsModule],
  template: `
    <div class="card">
      <h2>Enviar vídeo</h2>
      @if (cota(); as c) {
        <p class="suave">Cota de uploads: {{ c.used }} usados · {{ c.reserved }} em andamento · {{ c.available }} disponíveis (limite {{ c.limit }})</p>
      }
      <form (ngSubmit)="enviar()">
        <label>Título <input name="titulo" [(ngModel)]="titulo" required></label>
        <label>Arquivo <input type="file" accept="video/*" (change)="escolher($event)"></label>
        <button type="submit" [disabled]="enviando() || !arquivo || (cota() !== null && cota()!.available <= 0)">Enviar</button>
      </form>
      @if (erro()) { <p class="erro">{{ erro() }}</p> }
    </div>

    <div class="card">
      <div class="cab"><h2>Meus uploads</h2><button class="sec" (click)="carregar()">Atualizar</button></div>
      <p class="suave">Fluxo: PENDING → PROCESSING → PUBLISHED. Se a cota for rejeitada ou o processamento falhar, o vídeo vira FAILED e a vaga é devolvida.</p>
      <ul class="lista">
        @for (u of uploads(); track u.videoId) {
          <li><span>{{ u.title || u.videoId }}</span><span class="badge" [class]="u.status">{{ u.status }}</span></li>
        } @empty { <li class="suave">Nenhum upload ainda.</li> }
      </ul>
    </div>
  `,
})
export class UploadComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  titulo = '';
  arquivo: File | null = null;
  enviando = signal(false);
  erro = signal('');
  uploads = signal<UploadStatus[]>([]);
  cota = signal<Quota | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit() {
    this.carregar();
    this.carregarCota();
    // acompanha o status enquanto houver vídeo em andamento
    this.timer = setInterval(() => {
      if (this.uploads().some((u) => u.status === 'PENDING' || u.status === 'PROCESSING')) { this.carregar(); this.carregarCota(); }
    }, CONFIG.uploadPollMs);
  }
  ngOnDestroy() { clearInterval(this.timer); }

  escolher(e: Event) { this.arquivo = (e.target as HTMLInputElement).files?.[0] ?? null; }

  carregarCota() { this.api.quota().subscribe({ next: (c) => this.cota.set(c), error: () => {} }); }

  async carregar() {
    try { this.uploads.set(await firstValueFrom(this.api.myUploads())); } catch { /* mantém lista */ }
  }

  async enviar() {
    if (!this.arquivo) return;
    this.erro.set(''); this.enviando.set(true);
    try {
      await firstValueFrom(this.api.upload(this.arquivo, this.titulo));
      this.titulo = ''; this.arquivo = null;
      await this.carregar();
      this.carregarCota();
    } catch (e: any) {
      this.erro.set(`Falha no envio (${e?.status ?? 'sem resposta'})`);
    } finally { this.enviando.set(false); }
  }
}
