import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';

export default function UserList() {
  const [data, setData] = useState<any[]>([]);
  const [roles, setRoles] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState('');
  const [keyword, setKeyword] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState({ username: '', real_name: '', role_id: 0, password: '' });

  const loadRoles = async () => {
    try {
      const res = await api.get('/users/roles');
      const data = (res.code === 0 ? res.data : []) || [];
      setRoles(data);
      return data;
    } catch { return []; }
  };

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params: any = { page: 1, page_size: 100 };
      if (keyword) params.keyword = keyword;
      setData((await api.get('/users', { params })).data?.items || []);
    } catch (err: any) {
      setMsg('查询失败: ' + (err.response?.data?.message || err.message));
    } finally { setLoading(false); }
  }, [keyword]);

  useEffect(() => { fetchData(); }, []);

  const openCreate = async () => {
    setEditId(null);
    const rolesData = await loadRoles();
    setForm({ username: '', real_name: '', role_id: rolesData[0]?.id || 0, password: '' });
    setShowDialog(true);
  };

  const openEdit = async (user: any) => {
    setEditId(user.id);
    const rolesData = await loadRoles();
    setForm({ username: user.username, real_name: user.real_name || '', role_id: user.role_id, password: '' });
    setShowDialog(true);
  };

  const handleSubmit = async () => {
    if (!form.username || (!editId && !form.password)) return alert('请填写必填项');
    try {
      if (editId) {
        const payload: any = { real_name: form.real_name, role_id: form.role_id, status: 1 };
        if (form.password) payload.password = form.password;
        const res = await api.put(`/users/${editId}`, payload);
        if (res.code !== 0) { alert(res.message || '更新失败'); return; }
        setMsg('用户更新成功');
      } else {
        const res = await api.post('/users', form);
        if (res.code !== 0) { alert(res.message || '创建失败'); return; }
        setMsg('用户创建成功');
      }
      setShowDialog(false);
      fetchData();
    } catch (err: any) { alert(err.response?.data?.message || err.message || '操作失败'); }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除此用户？')) return;
    try {
      const res = await api.delete(`/users/${id}`);
      if (res.code !== 0) { alert(res.message || '删除失败'); return; }
      fetchData();
    } catch (err: any) { alert(err.response?.data?.message || err.message || '删除失败'); }
  };

  const handleResetPwd = async (id: number) => {
    const pwd = prompt('请输入新密码（至少4位）：');
    if (!pwd) return;
    if (pwd.length < 4) return alert('密码至少4位');
    try {
      const res = await api.put(`/users/${id}/reset-password`, { new_password: pwd });
      if (res.code !== 0) { alert(res.message || '重置失败'); return; }
      setMsg('密码已重置');
    } catch (err: any) { alert(err.response?.data?.message || err.message || '重置失败'); }
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">用户管理</h1>
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">新建用户</button>
        </div>
      </div>

      {/* Search */}
      <div className="bg-white rounded-lg shadow p-4 mb-4 flex gap-4 items-end">
        <div>
          <label className="block text-sm text-gray-600 mb-1">搜索</label>
          <input className="border rounded px-3 py-1.5 w-48" placeholder="用户名/姓名"
            value={keyword} onChange={e => setKeyword(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && fetchData()} />
        </div>
        <button onClick={fetchData} className="bg-blue-600 text-white px-4 py-1.5 rounded hover:bg-blue-700">查询</button>
      </div>

      {msg && <div className="bg-green-50 text-green-800 p-2 rounded mb-3 text-sm" onClick={() => setMsg('')}>{msg}</div>}

      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow">
          <thead><tr className="bg-gray-50 text-left text-sm">
            <th className="p-3">ID</th><th className="p-3">用户名</th><th className="p-3">姓名</th>
            <th className="p-3">角色</th><th className="p-3">状态</th>
            <th className="p-3">创建时间</th><th className="p-3 w-48">操作</th>
          </tr></thead>
          <tbody>{data.map(u => (
            <tr key={u.id} className="border-t hover:bg-gray-50 text-sm">
              <td className="p-3">{u.id}</td>
              <td className="p-3 font-mono">{u.username}</td>
              <td className="p-3">{u.real_name || '-'}</td>
              <td className="p-3">{u.role_name || u.role_code}</td>
              <td className="p-3">
                <span className={`px-2 py-0.5 rounded text-xs ${u.status === 1 ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>
                  {u.status === 1 ? '启用' : '禁用'}
                </span>
              </td>
              <td className="p-3 text-xs text-gray-500">{u.created_at?.slice(0, 19)}</td>
              <td className="p-3 space-x-1 whitespace-nowrap">
                <button onClick={() => openEdit(u)} className="text-blue-600 hover:text-blue-800 text-sm">编辑</button>
                <button onClick={() => handleResetPwd(u.id)} className="text-orange-500 hover:text-orange-700 text-sm">重置密码</button>
                <button onClick={() => handleDelete(u.id)} className="text-red-500 hover:text-red-700 text-sm">删除</button>
              </td>
            </tr>
          ))}</tbody>
        </table>
      )}

      {/* Create/Edit Dialog */}
      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[450px]">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑用户' : '新建用户'}</h2>
            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium mb-1">用户名</label>
                <input className="w-full border p-2 rounded" value={form.username}
                  disabled={!!editId} onChange={e => setForm({ ...form, username: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm font-medium mb-1">姓名</label>
                <input className="w-full border p-2 rounded" value={form.real_name}
                  onChange={e => setForm({ ...form, real_name: e.target.value })} />
              </div>
              <div>
                <label className="block text-sm font-medium mb-1">角色</label>
                <select className="w-full border p-2 rounded" value={form.role_id}
                  onChange={e => setForm({ ...form, role_id: Number(e.target.value) })}>
                  {roles.map((r: any) => <option key={r.id} value={r.id}>{r.role_name} ({r.role_code})</option>)}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium mb-1">密码{editId ? '（留空不修改）' : ''}</label>
                <input type="password" className="w-full border p-2 rounded" value={form.password}
                  placeholder={editId ? '留空则不修改密码' : ''}
                  onChange={e => setForm({ ...form, password: e.target.value })} />
              </div>
            </div>
            <div className="flex justify-end gap-3 mt-6">
              <button onClick={() => setShowDialog(false)} className="px-4 py-2 border rounded hover:bg-gray-50">取消</button>
              <button onClick={handleSubmit} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
                {editId ? '保存' : '创建'}
              </button>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
