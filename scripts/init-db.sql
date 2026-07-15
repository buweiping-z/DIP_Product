-- Roles
INSERT INTO Roles (Id, RoleCode, RoleName, Description, Status, CreatedAt, IsDeleted, CreatedBy, UpdatedBy, UpdatedAt) VALUES
(1, 'admin', '系统管理员', 'Full system access', 1, NOW(), 0, NULL, NULL, NULL),
(2, 'warehouse_manager', '仓库管理员', 'Warehouse management', 1, NOW(), 0, NULL, NULL, NULL),
(3, 'operator', '操作员', 'Line-side operations', 1, NOW(), 0, NULL, NULL, NULL),
(4, 'viewer', '查看员', 'Read-only access', 1, NOW(), 0, NULL, NULL, NULL);

-- Admin user (password: Admin@123, BCrypt hashed)
INSERT INTO Operators (Id, Username, RealName, PasswordHash, RoleId, Status, CreatedAt, IsDeleted, CreatedBy, UpdatedBy, UpdatedAt, LineId) VALUES
(1, 'admin', '系统管理员', '$2a$11$sU9s7YKZ3zLQ5Jk4X6vZLeJqZ3xK5Y8mR2nW1pA7bC9dE0fG2hI4', 1, 1, NOW(), 0, NULL, NULL, NULL, NULL);

-- Default production line
INSERT INTO ProductionLines (Id, LineNo, LineName, Capacity, Status, CreatedAt, IsDeleted, CreatedBy, UpdatedBy, UpdatedAt) VALUES
(1, 'SMT-01', 'SMT生产线1号', 5000, 1, NOW(), 0, NULL, NULL, NULL);

-- Default stations
INSERT INTO Stations (Id, StationNo, LineId, StationName, ProcessOrder, Status, CreatedAt, IsDeleted, CreatedBy, UpdatedBy, UpdatedAt) VALUES
(1, 'FEEDER-01', 1, '供料台1', 1, 1, NOW(), 0, NULL, NULL, NULL),
(2, 'PRINT-01', 1, '印刷台1', 2, 1, NOW(), 0, NULL, NULL, NULL),
(3, 'PLACE-01', 1, '贴片机1', 3, 1, NOW(), 0, NULL, NULL, NULL),
(4, 'REFLOW-01', 1, '回流焊1', 4, 1, NOW(), 0, NULL, NULL, NULL),
(5, 'AOI-01', 1, 'AOI检测1', 5, 1, NOW(), 0, NULL, NULL, NULL);

-- Default warehouse locations
INSERT INTO WarehouseLocations (Id, LocationCode, Warehouse, Zone, `Row`, `Column`, Layer, MaxCapacity, CurrentQty, Status, CreatedAt, IsDeleted, CreatedBy, UpdatedBy, UpdatedAt) VALUES
(1, 'WH-A-01-01-01', '线边仓', 'A', '01', '01', '01', 1000.00, 0.00, 1, NOW(), 0, NULL, NULL, NULL),
(2, 'WH-A-01-02-01', '线边仓', 'A', '01', '02', '01', 1000.00, 0.00, 1, NOW(), 0, NULL, NULL, NULL),
(3, 'WH-A-02-01-01', '线边仓', 'A', '02', '01', '01', 1000.00, 0.00, 1, NOW(), 0, NULL, NULL, NULL),
(4, 'WH-B-01-01-01', '线边仓', 'B', '01', '01', '01', 1000.00, 0.00, 1, NOW(), 0, NULL, NULL, NULL);
