// 自定义 Toast 提示，替代浏览器原生 alert
let toastTimer: number | null = null;

export function showToast(message: string, type: 'error' | 'success' | 'info' = 'error') {
  // 移除已有 toast
  const existing = document.getElementById('__toast__');
  if (existing) existing.remove();
  if (toastTimer) { clearTimeout(toastTimer); toastTimer = null; }

  const bgColor = type === 'error' ? 'bg-red-600' : type === 'success' ? 'bg-green-600' : 'bg-blue-600';

  const el = document.createElement('div');
  el.id = '__toast__';
  el.className = `fixed top-4 left-1/2 -translate-x-1/2 z-[9999] ${bgColor} text-white px-6 py-3 rounded-lg shadow-lg flex items-center gap-3 min-w-[300px] max-w-[500px] animate-toast-in`;
  el.innerHTML = `
    <span class="font-bold whitespace-nowrap">系统提示</span>
    <span class="flex-1 text-sm">${message}</span>
    <button class="text-white/70 hover:text-white text-lg leading-none ml-2" onclick="this.parentElement.remove()">&times;</button>
  `;
  document.body.appendChild(el);

  toastTimer = window.setTimeout(() => {
    el.classList.add('opacity-0', 'transition-opacity', 'duration-300');
    setTimeout(() => el.remove(), 300);
  }, 3000);
}

// 注入动画样式
const style = document.createElement('style');
style.textContent = `
  @keyframes toast-in {
    from { opacity: 0; transform: translate(-50%, -20px); }
    to { opacity: 1; transform: translate(-50%, 0); }
  }
  .animate-toast-in { animation: toast-in 0.25s ease-out; }
`;
document.head.appendChild(style);

// 全局快捷方法
(window as any).showToast = showToast;
