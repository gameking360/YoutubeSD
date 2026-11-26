import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiService, Quota } from '../../core/api.service';
import { InstancesService } from '../../core/instances';

@Component({
  selector: 'app-profile',
  imports: [FormsModule],
  template: `
    <div class="grid">
      <section>
        <div class="card">
          <h2>Meu perfil</h2>
          @if (api.usuario(); as u) {
            <dl class="detalhe">
              <dt>Nome</dt><dd>{{ u.name }}</dd>
              <dt>E-mail</dt><dd>{{ u.email }}</dd>
              <dt>Papel</dt><dd><span class="badge">{{ u.role }}</span></dd>
              <dt>Conta criada em</dt><dd>{{ data(u.createdAt) }}</dd>
              <dt>ID</dt><dd><code>{{ u.id }}</code></dd>
            </dl>
            <form (ngSubmit)="salvar()">
              <label>Alterar nome <input name="nome" [(ngModel)]="nome" maxlength="120" required [disabled]="salvando()"></label>
              <button type="submit" [disabled]="salvando() || !nome.trim() || nome.trim() === u.name">Salvar</button>
            </form>
          } @else { <p class="suave">Carregando…</p> }
          @if (erro()) { <p class="erro">{{ erro() }}</p> }
          @if (ok()) { <p class="ok">{{ ok() }}</p> }
        </div>
      </section>
      <aside>
        <div class="card">
          <div class="cab"><h2>Cota de uploads</h2><button class="sec" (click)="carregar()">Atualizar</button></div>
          @if (cota(); as c) {
            <progress [value]="c.used + c.reserved" [max]="c.limit || 1" style="width:100%"></progress>
            <dl class="detalhe">
              <dt>Limite</dt><dd>{{ c.limit }}</dd>
              <dt>Usados</dt><dd>{{ c.used }}</dd>
              <dt>Reservados (em andamento)</dt><dd>{{ c.reserved }}</dd>
              <dt>Disponíveis</dt><dd>{{ c.available }}</dd>
            </dl>
          } @else { <p class="suave">Sem dados de cota.</p> }
        </div>
        <div class="card">
          <h2>Réplicas que responderam</h2>
          <p class="suave">Último valor de X-Instance-Id recebido de cada serviço pelo gateway.</p>
          <dl class="detalhe">
            @for (r of replicas(); track r[0]) { <dt>{{ r[0] }}</dt><dd><code>{{ r[1] }}</code></dd> }
            @empty { <dd class="suave">Nenhuma ainda.</dd> }
          </dl>
        </div>
      </aside>
    </div>
  `,
})
export class ProfileComponent implements OnInit {
  api = inject(ApiService);
  private inst = inject(InstancesService);
  nome = '';
  cota = signal<Quota | null>(null);
  salvando = signal(false);
  erro = signal('');
  ok = signal('');

  replicas() { return Object.entries(this.inst.ultima()); }
  data(iso: string) { return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : iso + 'Z').toLocaleString('pt-BR'); }

  ngOnInit() { this.carregar(); }

  async carregar() {
    try {
      const u = await firstValueFrom(this.api.me());
      this.nome = u.name;
      if (u.quota) this.cota.set(u.quota);
    } catch (e: any) { this.erro.set(`Falha ao carregar o perfil (${e?.status ?? 'sem resposta'}).`); }
    try { this.cota.set(await firstValueFrom(this.api.quota())); } catch { /* fica com a do perfil */ }
  }

  async salvar() {
    this.erro.set(''); this.ok.set(''); this.salvando.set(true);
    try {
      await firstValueFrom(this.api.updateMe(this.nome.trim()));
      this.ok.set('Nome atualizado.');
    } catch (e: any) {
      const campos = e?.error?.errors ? Object.values(e.error.errors).flat().join(' ') : '';
      this.erro.set(campos || e?.error?.detail || `Falha (${e?.status ?? 'sem resposta'})`);
    } finally { this.salvando.set(false); }
  }
}
