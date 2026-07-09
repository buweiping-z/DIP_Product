import { Routes, Route, Navigate } from 'react-router-dom';
import Layout from './pages/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import OrderList from './pages/OrderList';
import InventoryList from './pages/InventoryList';
import PartList from './pages/PartList';
import LocationList from './pages/LocationList';
import PrepList from './pages/PrepList';
import ShelvingList from './pages/LoadingList';
import ReturnList from './pages/ReturnList';
import SubstituteList from './pages/SubstituteList';
import StockCountList from './pages/StockCountList';
import AbnormalList from './pages/AbnormalList';
import OnlineList from './pages/OnlineList';
import RefillList from './pages/RefillList';
import UserList from './pages/UserList';
import { isAuthenticated } from './lib/auth';

function PrivateRoute({ children }: { children: React.ReactNode }) {
  return isAuthenticated() ? <>{children}</> : <Navigate to="/login" />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={<PrivateRoute><Layout /></PrivateRoute>}>
        <Route index element={<Navigate to="/dashboard" />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="orders" element={<OrderList />} />
        <Route path="inventory" element={<InventoryList />} />
        <Route path="parts" element={<PartList />} />
        <Route path="locations" element={<LocationList />} />
        <Route path="prep" element={<PrepList />} />
        <Route path="refill" element={<RefillList />} />
        <Route path="shelving" element={<ShelvingList />} />
        <Route path="online" element={<OnlineList />} />
        <Route path="return" element={<ReturnList />} />
        <Route path="substitute" element={<SubstituteList />} />
        <Route path="stockcount" element={<StockCountList />} />
        <Route path="abnormal" element={<AbnormalList />} />
        <Route path="users" element={<UserList />} />
      </Route>
    </Routes>
  );
}
