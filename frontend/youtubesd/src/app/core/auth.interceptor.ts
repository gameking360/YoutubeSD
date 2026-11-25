import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { CONFIG } from './config';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const isApi = req.url.startsWith(CONFIG.gateway + '/api/');
  const isAuth = req.url.includes('/api/auth/');

  const comToken = (r: HttpRequest<unknown>) => {
    const t = auth.token();
    return t && isApi && !isAuth ? r.clone({ setHeaders: { Authorization: `Bearer ${t}` } }) : r;
  };

  return next(comToken(req)).pipe(
    catchError((e) => {
      if (e.status !== 401 || !isApi || isAuth) return throwError(() => e);
      // token expirado: renova uma vez e repete a chamada
      return from(auth.refresh()).pipe(
        switchMap((ok) => {
          if (ok) return next(comToken(req));
          router.navigateByUrl('/login');
          return throwError(() => e);
        }),
      );
    }),
  );
};
