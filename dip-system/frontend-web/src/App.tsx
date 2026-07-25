import { lazy, Suspense } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import Layout from './pages/Layout';
import Login from './pages/Login';
import { isAuthenticated } from './lib/auth';

const Dashboard = lazy(() => import('./pages/Dashboard'));
const OrderList = lazy(() => import('./pages/OrderList'));
const InventoryList = lazy(() => import('./pages/InventoryList'));
const PartList = lazy(() => import('./pages/PartList'));
const LocationList = lazy(() => import('./pages/LocationList'));
const PrepList = lazy(() => import('./pages/PrepList'));
const ShelvingList = lazy(() => import('./pages/LoadingList'));
const OutboundList = lazy(() => import('./pages/OutboundList'));
const ReturnList = lazy(() => import('./pages/ReturnList'));
const SubstituteList = lazy(() => import('./pages/SubstituteList'));
const StockCountList = lazy(() => import('./pages/StockCountList'));
const AbnormalList = lazy(() => import('./pages/AbnormalList'));
const OnlineList = lazy(() => import('./pages/OnlineList'));
const RefillList = lazy(() => import('./pages/RefillList'));
const ChangeoverList = lazy(() => import('./pages/ChangeoverList'));
const UserList = lazy(() => import('./pages/UserList'));

function PrivateRoute({ children }: { children: React.ReactNode }) {
  return isAuthenticated() ? <>{children}</> : <Navigate to="/login" />;
}

function PageLoader() {
  return <div className="flex items-center justify-center h-64 text-gray-400">加载中...</div>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<PrivateRoute><Layout /></PrivateRoute>}>
        <Route index element={<Navigate to="/dashboard" />} />
        <Route path="dashboard" element={<Suspense fallback={<PageLoader />}><Dashboard /></Suspense>} />
        <Route path="orders" element={<Suspense fallback={<PageLoader />}><OrderList /></Suspense>} />
        <Route path="inventory" element={<Suspense fallback={<PageLoader />}><InventoryList /></Suspense>} />
        <Route path="parts" element={<Suspense fallback={<PageLoader />}><PartList /></Suspense>} />
        <Route path="locations" element={<Suspense fallback={<PageLoader />}><LocationList /></Suspense>} />
        <Route path="prep" element={<Suspense fallback={<PageLoader />}><PrepList /></Suspense>} />
        <Route path="refill" element={<Suspense fallback={<PageLoader />}><RefillList /></Suspense>} />
        <Route path="shelving" element={<Suspense fallback={<PageLoader />}><ShelvingList /></Suspense>} />
        <Route path="online" element={<Suspense fallback={<PageLoader />}><OnlineList /></Suspense>} />
        <Route path="return" element={<Suspense fallback={<PageLoader />}><ReturnList /></Suspense>} />
        <Route path="substitute" element={<Suspense fallback={<PageLoader />}><SubstituteList /></Suspense>} />
        <Route path="outbound" element={<Suspense fallback={<PageLoader />}><OutboundList /></Suspense>} />
        <Route path="changeover" element={<Suspense fallback={<PageLoader />}><ChangeoverList /></Suspense>} />
        <Route path="stockcount" element={<Suspense fallback={<PageLoader />}><StockCountList /></Suspense>} />
        <Route path="abnormal" element={<Suspense fallback={<PageLoader />}><AbnormalList /></Suspense>} />
        <Route path="users" element={<Suspense fallback={<PageLoader />}><UserList /></Suspense>} />
      </Route>
    </Routes>
  );
}
