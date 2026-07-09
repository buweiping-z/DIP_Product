package com.dip.material.ui.viewmodels

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.*
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.PreferencesManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

// ===== Login =====
data class LoginUiState(
    val username: String = "",
    val password: String = "",
    val isLoading: Boolean = false,
    val error: String? = null,
    val isLoggedIn: Boolean = false
)

class LoginViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(LoginUiState())
    val state: StateFlow<LoginUiState> = _state.asStateFlow()

    fun login(username: String, password: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, error = null) }
            try {
                val res = repo.login(username, password)
                if (res.isSuccess && res.data != null) {
                    repo.context.let { ctx ->
                        val prefs = PreferencesManager(ctx)
                        kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.IO).launch {
                            prefs.saveTokens(res.data.accessToken, res.data.refreshToken)
                            prefs.saveUsername(username)
                        }
                    }
                    _state.update { it.copy(isLoggedIn = true, isLoading = false) }
                } else {
                    _state.update { it.copy(isLoading = false, error = res.message ?: "登录失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, error = e.message ?: "网络错误") }
            }
        }
    }
}

// ===== Home =====
data class HomeUiState(
    val stats: DashboardStats? = null,
    val isLoading: Boolean = false,
    val error: String? = null
)

class HomeViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    fun loadStats() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            try {
                val res = repo.getDashboardStats()
                if (res.isSuccess) _state.update { it.copy(stats = res.data, isLoading = false) }
                else _state.update { it.copy(isLoading = false, error = res.message) }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, error = e.message) }
            }
        }
    }
}

// ===== Prep =====
data class PrepUiState(
    val orders: List<PrepOrderItem> = emptyList(),
    val selectedOrder: PrepDetail? = null,
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val error: String? = null
)

class PrepViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(PrepUiState())
    val state: StateFlow<PrepUiState> = _state.asStateFlow()

    fun loadOrders() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            try {
                val res = repo.getPrepOrders(status = 1)
                if (res.isSuccess) _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) }
                else _state.update { it.copy(isLoading = false, error = res.message) }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, error = e.message) }
            }
        }
    }

    fun selectOrder(prepId: Int) {
        viewModelScope.launch {
            try {
                val res = repo.getPrepDetail(prepId)
                if (res.isSuccess) _state.update { it.copy(selectedOrder = res.data) }
            } catch (_: Exception) {}
        }
    }

    fun scanItem(barcode: String) {
        val prepId = _state.value.selectedOrder?.id ?: return
        viewModelScope.launch {
            try {
                val res = repo.scanPrepItem(prepId, barcode)
                if (res.isSuccess) {
                    _state.update { it.copy(scanMsg = "扫描成功: $barcode") }
                    selectOrder(prepId)
                } else {
                    _state.update { it.copy(scanMsg = res.message ?: "扫描失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(scanMsg = e.message ?: "扫描失败") }
            }
        }
    }

    fun checkKitComplete(prepId: Int) {
        viewModelScope.launch {
            try {
                val res = repo.checkKitComplete(prepId)
                _state.update { it.copy(scanMsg = if (res.isSuccess) "齐套检查完成" else (res.message ?: "检查失败")) }
            } catch (e: Exception) {
                _state.update { it.copy(scanMsg = e.message ?: "检查失败") }
            }
        }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
    fun clearSelection() { _state.update { it.copy(selectedOrder = null) } }
}

// ===== Refill =====
data class RefillUiState(
    val pendingItems: List<PendingItem> = emptyList(),
    val refillRecords: List<RefillRecord> = emptyList(),
    val isLoading: Boolean = false,
    val scanMsg: String? = null
)

class RefillViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(RefillUiState())
    val state: StateFlow<RefillUiState> = _state.asStateFlow()

    fun loadPending() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            try {
                val res = repo.getPendingItems()
                if (res.isSuccess) _state.update { it.copy(pendingItems = res.data ?: emptyList(), isLoading = false) }
                else _state.update { it.copy(isLoading = false) }
            } catch (_: Exception) { _state.update { it.copy(isLoading = false) } }
        }
    }

    fun loadRecords() {
        viewModelScope.launch {
            try {
                val res = repo.getRefillRecords()
                if (res.isSuccess) _state.update { it.copy(refillRecords = res.data?.items ?: emptyList()) }
            } catch (_: Exception) {}
        }
    }

    fun scanRefill(barcode: String, prepOrderId: Int, detailId: Int) {
        viewModelScope.launch {
            try {
                val res = repo.scanPrepItem(prepOrderId, barcode, detailId)
                if (res.isSuccess) {
                    _state.update { it.copy(scanMsg = "补料成功: $barcode") }
                    loadPending()
                } else {
                    _state.update { it.copy(scanMsg = res.message ?: "补料失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(scanMsg = e.message ?: "补料失败") }
            }
        }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}

// ===== Return =====
data class ReturnUiState(
    val records: List<ReturnOrderItem> = emptyList(),
    val scanMsg: String? = null
)

class ReturnViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(ReturnUiState())
    val state: StateFlow<ReturnUiState> = _state.asStateFlow()

    fun loadRecords() {
        viewModelScope.launch {
            try {
                val res = repo.getReturnList()
                if (res.isSuccess) _state.update { it.copy(records = res.data?.items ?: emptyList()) }
            } catch (_: Exception) {}
        }
    }

    fun scanReturn(barcode: String, locationId: Int) {
        viewModelScope.launch {
            try {
                val res = repo.scanReturn(barcode, locationId)
                if (res.isSuccess) {
                    _state.update { it.copy(scanMsg = "退料成功: $barcode") }
                    loadRecords()
                } else {
                    _state.update { it.copy(scanMsg = res.message ?: "退料失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(scanMsg = e.message ?: "退料失败") }
            }
        }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}

// ===== Shelving =====
data class ShelvingUiState(
    // Step 1: 扫部品
    val partBarcode: String = "",
    val scannedPart: PartItem? = null,
    val partLocations: List<InventoryAvailable> = emptyList(),
    val partLookupMsg: String? = null,
    // Step 2: 扫库位
    val locationBarcode: String = "",
    val scannedLocation: LocationItem? = null,
    val locationLookupMsg: String? = null,
    // Step 3: 输数量
    val quantity: String = "",
    // Flow
    val step: Int = 1,          // 1=扫部品, 2=扫库位, 3=输数量, 4=确认
    val isLoading: Boolean = false,
    val resultMsg: String? = null
)

class ShelvingViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(ShelvingUiState())
    val state: StateFlow<ShelvingUiState> = _state.asStateFlow()

    fun lookupPart(barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(partBarcode = barcode, isLoading = true, partLookupMsg = null) }
            try {
                val res = repo.searchParts(barcode)
                if (res.isSuccess) {
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty()) {
                        _state.update { it.copy(isLoading = false, partLookupMsg = "未找到部品: $barcode") }
                    } else {
                        val part = items.first()
                        // 同时查询库存分布
                        val invRes = repo.getAvailableInventory(part.id)
                        val locs = if (invRes.isSuccess) invRes.data ?: emptyList() else emptyList()
                        _state.update { it.copy(
                            scannedPart = part,
                            partLocations = locs,
                            isLoading = false,
                            step = 2
                        )}
                    }
                } else {
                    _state.update { it.copy(isLoading = false, partLookupMsg = res.message ?: "查询失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, partLookupMsg = e.message ?: "网络错误") }
            }
        }
    }

    fun lookupLocation(barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(locationBarcode = barcode, isLoading = true, locationLookupMsg = null) }
            try {
                val res = repo.searchLocations(barcode)
                if (res.isSuccess) {
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty()) {
                        _state.update { it.copy(isLoading = false, locationLookupMsg = "未找到库位: $barcode") }
                    } else {
                        _state.update { it.copy(
                            scannedLocation = items.first(),
                            isLoading = false,
                            step = 3
                        )}
                    }
                } else {
                    _state.update { it.copy(isLoading = false, locationLookupMsg = res.message ?: "查询失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, locationLookupMsg = e.message ?: "网络错误") }
            }
        }
    }

    fun onQuantityInput(qty: String) {
        _state.update { it.copy(quantity = qty) }
    }

    fun gotoConfirm() {
        val qty = _state.value.quantity.toDoubleOrNull()
        if (qty == null || qty <= 0) {
            _state.update { it.copy(resultMsg = "请输入有效数量") }
            return
        }
        _state.update { it.copy(step = 4) }
    }

    fun confirmShelving() {
        val s = _state.value
        val part = s.scannedPart ?: return
        val loc = s.scannedLocation ?: return
        val qty = s.quantity.toDoubleOrNull() ?: return

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            try {
                val res = repo.directShelving(part.partNo, loc.locationCode, qty)
                if (res.isSuccess) {
                    _state.update { it.copy(
                        isLoading = false,
                        resultMsg = "上架成功！${part.partNo} → ${loc.locationCode} × ${qty}",
                        // 重置回 Step 1
                        step = 1,
                        partBarcode = "", scannedPart = null, partLocations = emptyList(),
                        locationBarcode = "", scannedLocation = null,
                        quantity = ""
                    )}
                } else {
                    _state.update { it.copy(isLoading = false, resultMsg = res.message ?: "上架失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, resultMsg = e.message ?: "网络错误") }
            }
        }
    }

    fun resetToStep1() {
        _state.update { it.copy(
            step = 1, partBarcode = "", scannedPart = null, partLocations = emptyList(),
            partLookupMsg = null, locationBarcode = "", scannedLocation = null,
            locationLookupMsg = null, quantity = "", resultMsg = null
        )}
    }

    fun goToStep2() { _state.update { it.copy(step = 2) } }
    fun goToStep3() { _state.update { it.copy(step = 3) } }
    fun clearMsg() { _state.update { it.copy(resultMsg = null, partLookupMsg = null, locationLookupMsg = null) } }
}

// ===== Substitute =====
data class SubstituteUiState(
    val records: List<SubstituteRecordItem> = emptyList(),
    val scanMsg: String? = null
)

class SubstituteViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(SubstituteUiState())
    val state: StateFlow<SubstituteUiState> = _state.asStateFlow()

    fun loadRecords() {
        viewModelScope.launch {
            try {
                val res = repo.getSubstituteRecords()
                if (res.isSuccess) _state.update { it.copy(records = res.data?.items ?: emptyList()) }
            } catch (_: Exception) {}
        }
    }

    fun createSubstitute(originalPartId: Int, substitutePartId: Int,
                         sourceLocationId: Int, targetLocationId: Int, quantity: Double) {
        viewModelScope.launch {
            try {
                val res = repo.createSubstitute(originalPartId, substitutePartId, sourceLocationId, targetLocationId, quantity)
                if (res.isSuccess) {
                    _state.update { it.copy(scanMsg = "移库记录已创建") }
                    loadRecords()
                } else {
                    _state.update { it.copy(scanMsg = res.message ?: "创建失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(scanMsg = e.message ?: "创建失败") }
            }
        }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}

// ===== Online =====
data class OnlineUiState(
    val records: List<Any> = emptyList(),
    val scanMsg: String? = null
)

class OnlineViewModel(private val repo: AppRepository) : ViewModel() {
    private val _state = MutableStateFlow(OnlineUiState())
    val state: StateFlow<OnlineUiState> = _state.asStateFlow()

    fun loadRecords() {
        viewModelScope.launch {
            try {
                val res = repo.getOnlineRecords()
                if (res.isSuccess) _state.update { it.copy(records = res.data?.items ?: emptyList()) }
            } catch (_: Exception) {}
        }
    }

    fun confirmOnline(prepOrderId: Int, partNo: String, barcode: String) {
        viewModelScope.launch {
            try {
                val res = repo.confirmOnline(prepOrderId, partNo, barcode)
                if (res.isSuccess) {
                    _state.update { it.copy(scanMsg = "上线确认成功") }
                    loadRecords()
                } else {
                    _state.update { it.copy(scanMsg = res.message ?: "确认失败") }
                }
            } catch (e: Exception) {
                _state.update { it.copy(scanMsg = e.message ?: "确认失败") }
            }
        }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
