import api from './api';
export async function login(username: string, password: string) {
  const res = await api.post('/auth/login', { username, password });
  if (res.code === 0) {
    localStorage.setItem('token', res.data.access_token);
    localStorage.setItem('refreshToken', res.data.refresh_token);
    localStorage.setItem('user', JSON.stringify(res.data.user));
    return res.data.user;
  }
  throw new Error(res.message);
}
export function logout() { localStorage.clear(); window.location.href = '/login'; }
export function getUser() { try { return JSON.parse(localStorage.getItem('user') || ''); } catch { return null; } }
export function isAuthenticated() { return !!localStorage.getItem('token'); }
