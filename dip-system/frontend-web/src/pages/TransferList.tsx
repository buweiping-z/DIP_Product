import { useEffect, useState } from 'react';
import api from '../lib/api';
import HelpButton from '../lib/HelpButton';
const SMAP = ['', '待执行', '已执行', '已取消'];

export default function TransferList() {
  const [data, setData] = useState<any[]>([]);
  useEffect(() => { api.get('/transfer?page=1&page_size=50').then(r => setData(r.data?.items || [])); }, []);
  return (
    <div><div className="flex justify-between items-center mb-4">
        <h1 className="text-2xl font-bold">调拨管理</h1>
        <HelpButton title="调拨管理" sections={[
          { title: '功能概述', items: ['管理仓库间物料调拨', '查看调拨单状态和物料明细', '追溯调拨执行情况（待执行/已执行/已取消）'] },
          { title: '操作流程', items: ['创建调拨单，选择调拨物料和数量', '执行调拨操作完成库存转移', '确认调拨完成，库存自动更新'] }
        ]} />
      </div>
      <table className="w-full bg-white rounded-lg shadow"><thead><tr className="bg-gray-50 text-left text-sm">
        <th className="p-3">调拨单号</th><th className="p-3">状态</th><th className="p-3">物料数</th><th className="p-3">创建时间</th></tr></thead>
        <tbody>{data.map(o => (<tr key={o.id} className="border-t hover:bg-gray-50"><td className="p-3 font-mono text-sm">{o.order_no}</td>
          <td className="p-3">{SMAP[o.status] || o.status}</td><td className="p-3">{o.items?.length || 0}</td>
          <td className="p-3 text-sm text-gray-500">{o.created_at?.slice(0,19)}</td></tr>))}</tbody></table>
    </div>
  );
}
