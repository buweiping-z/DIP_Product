import axios, { type AxiosRequestConfig } from 'axios';
import { showToast } from './toast';

export interface ApiResponse<T = any> {
  code: number;
  data: T;
  message: string;
}

type ApiClient = {
  <T = any>(config: AxiosRequestConfig): Promise<ApiResponse<T>>;
  <T = any>(url: string, config?: AxiosRequestConfig): Promise<ApiResponse<T>>;
  get<T = any>(url: string, config?: AxiosRequestConfig): Promise<ApiResponse<T>>;
  post<T = any>(url: string, data?: any, config?: AxiosRequestConfig): Promise<ApiResponse<T>>;
  put<T = any>(url: string, data?: any, config?: AxiosRequestConfig): Promise<ApiResponse<T>>;
  delete<T = any>(url: string, config?: AxiosRequestConfig): Promise<ApiResponse<T>>;
};

const instance = axios.create({ baseURL: '/api/v1', timeout: 30000 });
instance.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});
instance.interceptors.response.use(
  (res) => {
    const body = res.data;
    // 全局业务错误拦截：非 0 code 且非 200 HTTP 状态显示提示
    if (body && body.code !== 0 && body.code !== undefined) {
      const msg = body.message || '操作失败';
      const isLogin = res.config.url?.includes('/auth/login');
      // 登录接口保留原始消息，不转换、不弹 toast
      if (isLogin) throw new Error(msg);
      const displayMsg = (body.code === 401 || body.code === 403)
        ? '当前用户无法操作'
        : msg;
      showToast(displayMsg, 'error');
      throw new Error(displayMsg);
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
            return instance(error.config);
          }
        } catch {}
      }
      localStorage.clear(); window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

const api = instance as unknown as ApiClient;
export default api;
