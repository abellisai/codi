import { InjectionToken } from '@angular/core';

export const ADMIN_API_PREFIX = '/api/v1/admin';
export const PUBLIC_API_PREFIX = '/api/v1/public';

/** Ogni app dichiara la propria area; nessun valore predefinito concede accesso all'admin. */
export const API_AREA = new InjectionToken<'admin' | 'public'>('Codi API area');
