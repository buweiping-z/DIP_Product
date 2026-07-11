import { useEffect, useState } from 'react';
import { Outlet, Link, useLocation } from 'react-router-dom';
import { logout } from '../lib/auth';
import api from '../lib/api';
import { Package, Box, Wrench, MapPin, ClipboardList, Truck, RotateCcw, ArrowLeftRight, ClipboardCheck, AlertTriangle, CheckCircle, User, Users, ArrowUpRight } from 'lucide-react';

const menu = [
  { path: '/dashboard', label: '仪表盘', icon: Package },
  { path: '/orders', label: '订单管理', icon: ClipboardList },
  { path: '/inventory', label: '库存管理', icon: Box },
  { path: '/parts', label: '物料管理', icon: Wrench },
  { path: '/locations', label: '库位管理', icon: MapPin },
  { path: '/prep', label: '备料管理', icon: Package },
  { path: '/refill', label: '补料管理', icon: Package },
  { path: '/shelving', label: '上架管理', icon: Truck },
  { path: '/online', label: '上线确认', icon: CheckCircle },
  { path: '/outbound', label: '出库管理', icon: ArrowUpRight },
  { path: '/return', label: '退料管理', icon: RotateCcw },
  { path: '/substitute', label: '替代料移库', icon: ArrowLeftRight },
  { path: '/stockcount', label: '盘点管理', icon: ClipboardCheck },
  { path: '/abnormal', label: '异常管理', icon: AlertTriangle },
  { path: '/users', label: '用户管理', icon: Users },
];

export default function Layout() {
  const location = useLocation();
  const [user, setUser] = useState<{ real_name: string; role_code: string; username: string } | null>(null);

  useEffect(() => {
    api.get('/auth/me').then(r => setUser(r.data)).catch(() => {});
  }, []);

  return (
    <div className="flex h-screen bg-gray-100">
      <aside className="w-56 bg-slate-800 text-white flex flex-col">
        <div className="p-4 text-lg font-bold border-b border-slate-700">DIP 物料管理</div>
        {user && (
          <div className="px-4 py-3 border-b border-slate-700 flex items-center gap-2">
            <User size={16} className="text-slate-400" />
            <div>
              <div className="text-sm font-medium">{user.real_name || user.username}</div>
              <div className="text-xs text-slate-400">{user.role_code || '普通用户'}</div>
            </div>
          </div>
        )}
        <nav className="flex-1 p-2">
          {menu.map(m => (
            <Link key={m.path} to={m.path}
              className={`flex items-center gap-2 px-3 py-2 rounded mb-1 text-sm ${location.pathname === m.path ? 'bg-slate-700' : 'hover:bg-slate-700'}`}>
              <m.icon size={18} /> {m.label}
            </Link>
          ))}
        </nav>
        <div className="p-4 border-t border-slate-700">
          <button onClick={logout} className="w-full text-left text-sm hover:text-red-300">退出登录</button>
        </div>
      </aside>
      <main className="flex-1 overflow-auto p-6"><Outlet /></main>
    </div>
  );
}
