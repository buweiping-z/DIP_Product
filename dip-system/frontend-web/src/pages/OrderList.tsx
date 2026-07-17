import { useEffect, useState, useRef } from 'react';
import api from '../lib/api';
import { showToast } from '../lib/toast';
import HelpButton from '../lib/HelpButton';

const STATUS_MAP = ['', '待备料', '待上线', '已完成', '已取消'];

interface ProductInfo {
  product_id: number;
  product_name: string;
  bom_count: number;
  bom_signature: string;
}

interface SelectedProduct {
  product_id: number;
  product_name: string;
  bom_count: number;
  bom_signature: string;
  plan_qty: number;
}

export default function OrderList() {
  const [data, setData] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState('');
  const [showDialog, setShowDialog] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const bomFileRef = useRef<HTMLInputElement>(null);
  const [products, setProducts] = useState<ProductInfo[]>([]);
  const [lines, setLines] = useState<any[]>([]);
  const [bomItems, setBomItems] = useState<any[]>([]);
  const [form, setForm] = useState({ line_id: 1, product_name: '', plan_qty: 1, priority: 2 });
  const [selectedProducts, setSelectedProducts] = useState<SelectedProduct[]>([]);
  const [productSearch, setProductSearch] = useState('');
  const [showDropdown, setShowDropdown] = useState(false);
  const [bomPreview, setBomPreview] = useState<any[]>([]);
  const [detailData, setDetailData] = useState<any>(null);
  const [showDetail, setShowDetail] = useState(false);

  const fetchData = async () => {
    setLoading(true);
    try { setData((await api.get('/orders?page=1&page_size=50')).data?.items || []); }
    finally { setLoading(false); }
  };
  useEffect(() => { fetchData(); }, []);

  const loadMeta = async () => {
    try {
      const [pRes, lRes] = await Promise.all([api.get('/orders/products'), api.get('/lines')]);
      setProducts((pRes.data || []) as ProductInfo[]);
      setLines(lRes.data || []);
      return lRes.data || [];
    } catch { return []; }
  };

  const openCreate = async () => {
    setEditId(null);
    setBomItems([]);
    setSelectedProducts([]);
    setProductSearch('');
    const loadedLines = await loadMeta();
    setForm({ line_id: loadedLines[0]?.id || 1, product_name: '', plan_qty: 1, priority: 2 });
    setShowDialog(true);
  };

  const openEdit = async (order: any) => {
    setEditId(order.id);
    setSelectedProducts([]);
    setProductSearch('');
    setForm({ line_id: order.line_id, product_name: order.product_name, plan_qty: order.plan_qty, priority: order.priority });

    // 并行加载所有数据（用局部变量，避免 React state 闭包陷阱）
    try {
      const [pRes, lRes, bomRes, detailRes] = await Promise.all([
        api.get('/orders/products'),
        api.get('/lines'),
        api.get(`/orders/${order.id}/bom-status`),
        api.get(`/orders/${order.id}/details`)
      ]);
      setProducts((pRes.data || []) as ProductInfo[]);
      setLines(lRes.data || []);
      setBomItems((bomRes.data || []).map((item: any) => ({ ...item, stock: item.net })));

      // 用局部变量 prods 而非 state，确保读到最新值
      const prods = (pRes.data || []) as ProductInfo[];
      const ops = detailRes.data?.order_products || [];
      const enriched = ops.map((op: any) => {
        const prod = prods.find((p: ProductInfo) => p.product_name === op.product_name);
        return { ...op, bom_count: prod?.bom_count || 0, bom_signature: prod?.bom_signature || '' };
      });
      setSelectedProducts(enriched);
    } catch { setBomItems([]); }
    setShowDialog(true);
  };

  const onProductChange = async (name: string) => {
    setForm({ ...form, product_name: name });
    if (!name) { setBomItems([]); return; }
    try {
      const res = await api.get('/orders/product-bom', { params: { name } });
      setBomItems((res.data || []).map((item: any) => ({ ...item, stock: item.stock || 0 })));
    } catch { setBomItems([]); }
  };

  // 模糊搜索过滤
  const filteredProducts = products.filter(p =>
    p.product_name.toLowerCase().includes(productSearch.toLowerCase()) ||
    (p.bom_count !== undefined && p.product_name.includes(productSearch))
  );

  // 添加产品到表格
  const addProduct = (prod: ProductInfo) => {
    if (selectedProducts.some(sp => sp.product_name === prod.product_name)) return; // 已存在
    setSelectedProducts([...selectedProducts, {
      product_id: prod.product_id,
      product_name: prod.product_name,
      bom_count: prod.bom_count,
      bom_signature: prod.bom_signature,
      plan_qty: 1
    }]);
    setProductSearch('');
    setShowDropdown(false);
  };

  // 删除已选产品
  const removeProduct = (idx: number) => {
    setSelectedProducts(selectedProducts.filter((_, i) => i !== idx));
  };

  // 修改计划数量
  const updatePlanQty = (idx: number, qty: number) => {
    const updated = [...selectedProducts];
    updated[idx] = { ...updated[idx], plan_qty: qty };
    setSelectedProducts(updated);
  };

  // 按 bom_signature 分组预览（同 BOM 料号集合的为一组）
  const groupPreview = (() => {
    const groups = new Map<string, SelectedProduct[]>();
    selectedProducts.forEach(sp => {
      const key = sp.bom_signature || `unknown_${sp.product_name}`;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key)!.push(sp);
    });
    return Array.from(groups.entries());
  })();

  // 编辑模式下的 BOM 一致性检测
  const allSameBomSignature = selectedProducts.length <= 1
    || new Set(selectedProducts.map(p => p.bom_signature)).size === 1;

  // 创建模式：实时计算合并 BOM 清单预览
  useEffect(() => {
    if (editId || selectedProducts.length === 0) { setBomPreview([]); return; }
    let cancelled = false;
    (async () => {
      const merged: Record<string, { part_no: string; required: number; stock: number }> = {};
      for (const sp of selectedProducts) {
        try {
          const res = await api.get('/orders/product-bom', { params: { name: sp.product_name } });
          for (const item of (res.data || [])) {
            const key = item.part_no;
            const req = (item.quantity || 0) * sp.plan_qty;
            if (merged[key]) {
              merged[key].required += req;
            } else {
              merged[key] = { part_no: item.part_no, required: req, stock: item.stock || 0 };
            }
          }
        } catch {}
      }
      if (!cancelled) setBomPreview(Object.values(merged));
    })();
    return () => { cancelled = true; };
  }, [selectedProducts, editId]);

  const handleSubmit = async () => {
    if (selectedProducts.length === 0) return alert('请至少选择一个产品');
    if (editId && !allSameBomSignature) return alert('编辑后的产品 BOM 不一致，请删除当前订单并重新创建');

    try {
      const payload = {
        line_id: form.line_id,
        priority: form.priority,
        products: selectedProducts.map(sp => ({
          product_id: sp.product_id,
          product_name: sp.product_name,
          plan_qty: sp.plan_qty
        }))
      };
      if (editId) {
        await api.put(`/orders/${editId}`, payload);
        showToast('订单更新成功', 'success');
      } else {
        const res = await api.post('/orders', payload);
        const total = res.data?.total || 1;
        showToast(`订单创建成功！已生成 ${total} 个订单`, 'success');
      }
      setShowDialog(false);
      fetchData();
    } catch {}
  };

  const handleStatusChange = async (id: number, status: number) => {
    try { await api.put(`/orders/${id}/status`, { status }); fetchData(); } catch {}
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确认删除此订单？')) return;
    try { await api.delete(`/orders/${id}`); fetchData(); } catch {}
  };

  const fetchDetail = async (id: number) => {
    try {
      const res = await api.get(`/orders/${id}/details`);
      setDetailData(res.data || {});
      setShowDetail(true);
    } catch {}
  };

  const handleBomImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]; if (!file) return;
    const fd = new FormData(); fd.append('file', file);
    try {
      const res = await api.post('/orders/import-bom', fd, { headers: { 'Content-Type': 'multipart/form-data' }, timeout: 60000 });
      setMsg(`BOM 导入成功: ${res.data?.count || 0} 条`); fetchData();
    } catch (err: any) { setMsg('导入失败: ' + (err.response?.data?.message || err.message)); }
    e.target.value = '';
  };

  return (
    <div>
      <div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">订单管理</h1>
        <HelpButton title="订单管理" sections={[
          { title: '功能概述', items: ['管理生产订单：创建、编辑、查看详情、导入BOM', '订单状态自动流转：待备料(1)→待上线(2)→已完成(3)，已取消(4)', '已完成和已取消的订单不可编辑或删除'] },
          { title: '操作流程', items: ['1. 新建订单：选产线→选产品→设计划数量→确认创建（自动生成备料单）', '2. 编辑订单：修改计划数量会联动更新备料需求', '3. 导入产品BOM：下载模板→填写料号/用量→上传', '4. 手机端备料完成后自动变为"待上线"，上线完成后自动变为"已完成"'] }
        ]} />
        <div className="flex gap-2">
          <button onClick={openCreate} className="bg-blue-600 text-white px-4 py-2 rounded hover:bg-blue-700">新建订单</button>
          <button onClick={() => bomFileRef.current?.click()} className="bg-green-600 text-white px-4 py-2 rounded hover:bg-green-700">导入产品BOM</button>
          <a href="/api/v1/orders/bom-template" className="bg-gray-500 text-white px-4 py-2 rounded hover:bg-gray-600">下载BOM模板</a>
          <input ref={bomFileRef} type="file" accept=".xlsx,.xls" className="hidden" onChange={handleBomImport} />
        </div>
      </div>
      {msg && <div className="bg-blue-50 text-blue-800 p-2 rounded mb-3 text-sm">{msg}</div>}

      {loading ? <p>加载中...</p> : (
        <table className="w-full bg-white rounded-lg shadow">
          <thead><tr className="bg-gray-50 text-left text-sm">
            <th className="p-3">订单号</th><th className="p-3">产品名称</th><th className="p-3">计划数量</th>
            <th className="p-3">优先级</th><th className="p-3">状态</th><th className="p-3">创建时间</th><th className="p-3 w-56">操作</th>
          </tr></thead>
          <tbody>{data.map(o => (
            <tr key={o.id} className="border-t hover:bg-gray-50">
              <td className="p-3 text-blue-600 font-mono text-sm">{o.order_no}</td>
              <td className="p-3">{o.product_name}</td>
              <td className="p-3">{o.plan_qty}</td>
              <td className="p-3">{['', '低', '中', '高'][o.priority] || o.priority}</td>
              <td className="p-3">{STATUS_MAP[o.status] || o.status}</td>
              <td className="p-3 text-sm text-gray-500">{o.created_at?.slice(0, 19)}</td>
              <td className="p-3 space-x-1 whitespace-nowrap">
                <button onClick={() => fetchDetail(o.id)} className="text-blue-600 hover:text-blue-800 text-sm">详情</button>
                {o.status !== 3 && o.status !== 4 && (
                  <>
                    <button onClick={() => openEdit(o)} className="text-blue-600 hover:text-blue-800 text-sm">编辑</button>
                    <button onClick={() => handleDelete(o.id)} className="text-red-500 hover:text-red-700 text-sm">删除</button>
                  </>
                )}
              </td>
            </tr>
          ))}</tbody>
        </table>
      )}

      {/* Create/Edit Dialog */}
      {showDialog && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[700px] max-h-[90vh] overflow-auto">
            <h2 className="text-xl font-bold mb-4">{editId ? '编辑订单' : '新建订单'}</h2>
            <div className="grid grid-cols-2 gap-4 mb-4">
              <div>
                <label className="block text-sm font-medium mb-1">产线</label>
                <select className="w-full border p-2 rounded" value={form.line_id}
                  onChange={e => setForm({ ...form, line_id: Number(e.target.value) })}>
                  {lines.map((l: any) => <option key={l.id} value={l.id}>{l.line_name}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium mb-1">优先级</label>
                <select className="w-full border p-2 rounded" value={form.priority}
                  onChange={e => setForm({ ...form, priority: Number(e.target.value) })}>
                  <option value={3}>高</option><option value={2}>中</option><option value={1}>低</option>
                </select>
              </div>
            </div>

            {/* 产品搜索栏 */}
            <div className="mb-4">
              <label className="block text-sm font-medium mb-1">搜索产品</label>
              <div className="relative">
                <input
                  type="text"
                  className="w-full border p-2 rounded"
                  placeholder="输入产品名称模糊搜索..."
                  value={productSearch}
                  onChange={e => { setProductSearch(e.target.value); setShowDropdown(true); }}
                  onFocus={() => setShowDropdown(true)}
                  onBlur={() => setTimeout(() => setShowDropdown(false), 200)}
                />
                {showDropdown && productSearch && (
                  <div className="absolute z-10 w-full bg-white border rounded-b shadow-lg max-h-48 overflow-auto">
                    {filteredProducts.length === 0 ? (
                      <div className="p-2 text-gray-400 text-sm">无匹配产品</div>
                    ) : (
                      filteredProducts.map(p => (
                        <div
                          key={p.product_name}
                          className={`px-3 py-2 cursor-pointer hover:bg-blue-50 flex justify-between ${
                            p.bom_count === 0 ? 'text-gray-300 cursor-not-allowed' : ''
                          } ${selectedProducts.some(sp => sp.product_name === p.product_name) ? 'bg-green-50' : ''}`}
                          onMouseDown={() => { if (p.bom_count > 0) addProduct(p); }}
                        >
                          <span>{p.product_name}</span>
                          <span className="text-xs text-gray-400">
                            {p.bom_count === 0 ? '无BOM' : `${p.bom_count} 种料号`}
                            {selectedProducts.some(sp => sp.product_name === p.product_name) && ' ✓'}
                          </span>
                        </div>
                      ))
                    )}
                  </div>
                )}
              </div>
            </div>

            {/* 已选产品表格 */}
            {selectedProducts.length > 0 && (
              <div className="mb-4">
                <h3 className="font-medium mb-2">已选产品</h3>
                <table className="w-full border text-sm">
                  <thead><tr className="bg-gray-100">
                    <th className="p-2 text-left">产品</th>
                    <th className="p-2 text-center">BOM料号数</th>
                    <th className="p-2 text-right">计划数量</th>
                    <th className="p-2 text-center">操作</th>
                  </tr></thead>
                  <tbody>
                    {selectedProducts.map((sp, idx) => (
                      <tr key={idx} className={`border-t ${editId && !allSameBomSignature && sp.bom_count !== selectedProducts[0]?.bom_count ? 'bg-red-50' : ''}`}>
                        <td className="p-2">{sp.product_name}</td>
                        <td className="p-2 text-center">{sp.bom_count}</td>
                        <td className="p-2 text-right">
                          <input
                            type="number"
                            className="w-20 border p-1 rounded text-right"
                            min={1}
                            value={sp.plan_qty}
                            onChange={e => updatePlanQty(idx, Number(e.target.value) || 1)}
                          />
                        </td>
                        <td className="p-2 text-center">
                          <button onClick={() => removeProduct(idx)}
                            className="text-red-500 hover:text-red-700 text-lg leading-none">&times;</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {/* 分组预览 */}
            {selectedProducts.length > 0 && (
              <div className="mb-4 p-3 bg-gray-50 rounded text-sm">
                {editId && !allSameBomSignature ? (
                  <p className="text-red-600 font-medium">⚠ 编辑后的产品 BOM 不一致，请删除当前订单并重新创建</p>
                ) : (
                  <p className="text-gray-600">
                    将生成 <strong>{groupPreview.length}</strong> 个订单：
                    {groupPreview.map(([signature, prods], gi) => (
                      <span key={gi}>
                        {gi > 0 && '；'}
                        订单{gi + 1}: {prods.map(p => p.product_name).join(' / ')}
                        {groupPreview.length > 1 && `（${prods[0]?.bom_count || '?'}种料号）`}
                      </span>
                    ))}
                  </p>
                )}
              </div>
            )}

            {/* 创建模式：合并 BOM 清单预览 */}
            {!editId && bomPreview.length > 0 && (
              <div className="mb-4">
                <h3 className="font-medium mb-2">合并 BOM 清单预览</h3>
                <table className="w-full border text-sm">
                  <thead><tr className="bg-gray-100">
                    <th className="p-2 text-left">#</th>
                    <th className="p-2 text-left">料号</th>
                    <th className="p-2 text-right">总需求</th>
                    <th className="p-2 text-right">可用库存</th>
                    <th className="p-2 text-center">状态</th>
                  </tr></thead>
                  <tbody>{bomPreview.map((item: any, idx: number) => (
                    <tr key={idx} className={`border-t ${item.stock < item.required ? 'bg-red-50' : ''}`}>
                      <td className="p-2">{idx + 1}</td>
                      <td className="p-2 font-mono">{item.part_no}</td>
                      <td className="p-2 text-right">{item.required}</td>
                      <td className="p-2 text-right">{item.stock}</td>
                      <td className={`p-2 text-center text-xs ${item.stock >= item.required ? 'text-green-600' : 'text-red-600 font-medium'}`}>
                        {item.stock >= item.required ? '充足' : `缺 ${item.required - item.stock}`}
                      </td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
            )}

            {/* 编辑模式展示已有 BOM 状态 */}
            {editId && bomItems.length > 0 && (
              <div className="mb-4">
                <h3 className="font-medium mb-2">BOM 物料清单</h3>
                <table className="w-full border text-sm">
                  <thead><tr className="bg-gray-100">
                    <th className="p-2 text-left">#</th><th className="p-2 text-left">料号</th>
                    <th className="p-2 text-right">总需求</th>
                    <th className="p-2 text-right">已冻结</th>
                    <th className="p-2 text-right">可用库存</th>
                    <th className="p-2 text-center">状态</th>
                  </tr></thead>
                  <tbody>{bomItems.map((item: any, idx: number) => (
                    <tr key={idx} className={`border-t ${(item.net || 0) < 0 ? 'bg-red-50' : ''}`}>
                      <td className="p-2">{idx + 1}</td><td className="p-2 font-mono">{item.part_no}</td>
                      <td className="p-2 text-right">{item.required_qty || 0}</td>
                      <td className="p-2 text-right text-blue-600">{item.frozen_qty || 0}</td>
                      <td className="p-2 text-right">{item.available_qty || 0}</td>
                      <td className={`p-2 text-center text-xs ${(item.net || 0) >= 0 ? 'text-green-600' : 'text-red-600 font-medium'}`}>
                        {(item.net || 0) >= 0 ? '充足' : `缺 ${Math.abs(item.net)}`}
                      </td>
                    </tr>
                  ))}</tbody>
                </table>
              </div>
            )}

            <div className="flex justify-end gap-3">
              <button onClick={() => setShowDialog(false)} className="px-4 py-2 border rounded hover:bg-gray-50">取消</button>
              <button onClick={handleSubmit} className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
                {editId ? '保存修改' : '确认创建'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Detail Dialog */}
      {showDetail && detailData && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg p-6 w-[800px] max-h-[90vh] overflow-auto">
            <div className="flex justify-between items-center mb-4">
              <h2 className="text-xl font-bold">订单详情</h2>
              <button onClick={() => { setShowDetail(false); setDetailData(null); }}
                className="text-gray-400 hover:text-gray-600 text-2xl leading-none">&times;</button>
            </div>

            {/* Basic Info */}
            <div className="grid grid-cols-4 gap-4 mb-6">
              <div><span className="text-gray-500 text-sm">订单号</span><p className="font-mono text-sm">{detailData.order_no}</p></div>
              <div><span className="text-gray-500 text-sm">产线</span><p>{detailData.line_name || detailData.line_id}</p></div>
              <div><span className="text-gray-500 text-sm">产品名称</span><p className="font-medium">{detailData.product_name}</p></div>
              <div><span className="text-gray-500 text-sm">计划数量</span><p>{detailData.plan_qty}</p></div>
              <div><span className="text-gray-500 text-sm">优先级</span><p>{['', '低', '中', '高'][detailData.priority] || detailData.priority}</p></div>
              <div><span className="text-gray-500 text-sm">状态</span><p>{STATUS_MAP[detailData.status] || detailData.status}</p></div>
              <div><span className="text-gray-500 text-sm">创建时间</span><p className="text-sm">{detailData.created_at?.slice(0, 19)}</p></div>
              <div><span className="text-gray-500 text-sm">客户订单号</span><p>{detailData.customer_order_no || '-'}</p></div>
            </div>

            {/* Order Products */}
            {detailData.order_products && detailData.order_products.length > 0 && (
              <>
                <h3 className="font-medium mb-2">产品明细</h3>
                <table className="w-full border text-sm mb-4">
                  <thead><tr className="bg-gray-100">
                    <th className="p-2 text-left">产品名称</th>
                    <th className="p-2 text-right">计划数量</th>
                  </tr></thead>
                  <tbody>{detailData.order_products.map((op: any, idx: number) => (
                    <tr key={idx} className="border-t">
                      <td className="p-2">{op.product_name}</td>
                      <td className="p-2 text-right">{op.plan_qty}</td>
                    </tr>
                  ))}</tbody>
                </table>
              </>
            )}

            {/* BOM Items */}
            <h3 className="font-medium mb-2">BOM 物料清单</h3>
            {(detailData.bom_items || []).length > 0 ? (
              <table className="w-full border text-sm mb-6">
                <thead><tr className="bg-gray-100">
                  <th className="p-2 text-left">#</th><th className="p-2 text-left">料号</th>
                  <th className="p-2 text-right">需求数量</th><th className="p-2 text-left">位号</th>
                </tr></thead>
                <tbody>{(detailData.bom_items || []).map((item: any, idx: number) => (
                  <tr key={idx} className="border-t">
                    <td className="p-2">{idx + 1}</td>
                    <td className="p-2 font-mono">{item.part_no}</td>
                    <td className="p-2 text-right">{item.required_qty}</td>
                    <td className="p-2 text-sm text-gray-500">{item.reference_designator || '-'}</td>
                  </tr>
                ))}</tbody>
              </table>
            ) : <p className="text-gray-400 text-sm mb-6">无 BOM 数据</p>}

            {/* Prep Orders */}
            <h3 className="font-medium mb-2">关联备料单</h3>
            {(detailData.prep_orders || []).length > 0 ? (
              <table className="w-full border text-sm">
                <thead><tr className="bg-gray-100">
                  <th className="p-2 text-left">备料单号</th><th className="p-2 text-center">状态</th><th className="p-2 text-center">齐套结果</th>
                </tr></thead>
                <tbody>{(detailData.prep_orders || []).map((p: any) => (
                  <tr key={p.id} className="border-t">
                    <td className="p-2 font-mono text-sm">{p.order_no}</td>
                    <td className="p-2 text-center">{STATUS_MAP[p.status] || p.status}</td>
                    <td className="p-2 text-center">{p.kit_check_result || '-'}</td>
                  </tr>
                ))}</tbody>
              </table>
            ) : <p className="text-gray-400 text-sm">无关联备料单</p>}
          </div>
        </div>
      )}
    </div>
  );
}
