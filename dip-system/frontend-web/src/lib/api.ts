import axios from 'axios';
import { showToast } from './toast';
const api = axios.create({ baseURL: '/api/v1', timeout: 30000 });
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});
api.interceptors.response.use(
  (res) => {
    const body = res.data;
    // 全局业务错误拦截：非 0 code 且非 200 HTTP 状态显示提示
    if (body && body.code !== 0 && body.code !== undefined) {
      const msg = body.message || '操作失败';
      // 权限相关错误统一替换为友好提示
      const displayMsg = (body.code === 401 || body.code === 403)
        ? '当前用户无法操作'
        : msg;
      showToast(displayMsg, 'error');
    }
    return body;
  },
  async (error) => {
    if (error.response?.status === 401 && !error.config._retry) {
      error.config._retry = true;
      const refreshToken = localStorage.getItem('refreshToken');
      if (refreshToken) {
        try {
          const res = await axios.post('/api/v1/auth/refresh', { refresh_token: refreshToken });
          if (res.data.code === 0) {
            const d = res.data.data;
            localStorage.setItem('token', d.access_token);
            localStorage.setItem('refreshToken', d.refresh_token);
            error.config.headers.Authorization = `Bearer ${d.access_token}`;
            return api(error.config);
          }
        } catch {}
      }
      localStorage.clear(); window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);
export default api;
