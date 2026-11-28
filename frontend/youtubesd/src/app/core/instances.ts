import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { tap } from 'rxjs';

/** Última réplica (header X-Instance-Id) que respondeu por serviço: mostra o balanceamento do gateway. */
@Injectable({ providedIn: 'root' })
export class InstancesService {
  readonly ultima = signal<Record<string, string>>({});
  registrar(servico: string, instancia: string) {
    if (this.ultima()[servico] !== instancia) this.ultima.update((m) => ({ ...m, [servico]: instancia }));
  }
}

export const instanceInterceptor: HttpInterceptorFn = (req, next) => {
  const inst = inject(InstancesService);
  const servico = /\/api\/([^/?]+)/.exec(req.url)?.[1];
  return next(req).pipe(
    tap((ev) => {
      if (!servico || !(ev instanceof HttpResponse)) return;
      const id = ev.headers.get('X-Instance-Id');
      if (id) inst.registrar(servico, id);
    }),
  );
};
