import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="card narrow">
      <h2>{{ modoCadastro() ? 'Criar conta' : 'Entrar' }}</h2>
      <form (ngSubmit)="enviar()">
        @if (modoCadastro()) {
          <label>Nome <input name="nome" [(ngModel)]="nome" required></label>
        }
        <label>E-mail <input name="email" type="email" [(ngModel)]="email" required></label>
        <label>Senha <input name="senha" type="password" [(ngModel)]="senha" required></label>
        <button type="submit" [disabled]="carregando()">{{ modoCadastro() ? 'Cadastrar' : 'Entrar' }}</button>
      </form>
      @if (erro()) { <p class="erro">{{ erro() }}</p> }
      @if (msg()) { <p class="ok">{{ msg() }}</p> }
      <button class="link" type="button" (click)="modoCadastro.set(!modoCadastro())">
        {{ modoCadastro() ? 'Já tenho conta' : 'Criar uma conta' }}
      </button>
    </div>
  `,
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);
  nome = ''; email = ''; senha = '';
  modoCadastro = signal(false);
  carregando = signal(false);
  erro = signal('');
  msg = signal('');

  async enviar() {
    this.erro.set(''); this.msg.set(''); this.carregando.set(true);
    try {
      if (this.modoCadastro()) {
        await this.auth.register(this.nome, this.email, this.senha);
        this.msg.set('Conta criada. Faça login.');
        this.modoCadastro.set(false);
      } else {
        await this.auth.login(this.email, this.senha);
        this.router.navigateByUrl('/watch');
      }
    } catch (e: any) {
      this.erro.set(e?.error?.detail || e?.error?.message || `Falha (${e?.status ?? 'sem resposta'})`);
    } finally { this.carregando.set(false); }
  }
}
