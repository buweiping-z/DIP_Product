package com.dip.material.ui.online

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.OrderItem
import com.dip.material.data.models.PrepDetailItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class OnlineUiState(
    val orders: List<OrderItem> = emptyList(),
    val selectedOrder: OrderItem? = null,
    val details: List<PrepDetailItem> = emptyList(),
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val allDone: Boolean = false
)

class OnlineViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(OnlineUiState())
    val state: StateFlow<OnlineUiState> = _state.asStateFlow()

    init { loadOrders() }

    fun loadOrders() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getOrders(status = 2).fold(
                onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 选订单 → 加载备料明细 + 已上线消耗量，自动检测全部完成 */
    fun selectOrder(order: OrderItem) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, selectedOrder = order, allDone = false) }
            repo.getOrderDetail(order.id).fold(
                onSuccess = { res ->
                    val prepOrders = res.data?.prepOrders ?: emptyList()
                    if (prepOrders.isEmpty()) {
                        _state.update { it.copy(isLoading = false, scanMsg = "该订单无备料单") }
                        return@fold
                    }
                    val allDetails = mutableListOf<PrepDetailItem>()
                    for (p in prepOrders) {
                        repo.getPrepDetail(p.id).fold(
                            onSuccess = { pr -> pr.data?.details?.let { allDetails.addAll(it) } },
                            onFailure = {}
                        )
                    }
                    // 基于后端真实消耗量判断完成状态
                    val done = allDetails.isNotEmpty() && allDetails.all { it.onlineConsumedQty >= it.totalRequiredQty }
                    _state.update { it.copy(isLoading = false, details = allDetails, allDone = done) }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    fun scanOnline(barcode: String) {
        val trimmed = barcode.trim()
        val s = _state.value
        val match = s.details.firstOrNull {
            it.partNo.trim().equals(trimmed, ignoreCase = true)
        }
        if (match == null) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "未匹配到料号: $trimmed") }
            return
        }
        if (match.onlineConsumedQty >= match.totalRequiredQty) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "${match.partNo} 已全部确认") }
            return
        }

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }
            repo.confirmOnline(detailId = match.id.toLong(), barcode = trimmed).fold(
                onSuccess = { res ->
                    if (res.code == 0) {
                        ScanSoundManager.playSuccess()
                        // 刷新数据（后端已记录消耗量，重加载即可反映）
                        selectOrder(s.selectedOrder!!)
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res.message ?: "确认失败") }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message) }
                }
            )
        }
    }

    fun clearSelection() {
        _state.update { it.copy(selectedOrder = null, details = emptyList(), allDone = false) }
        loadOrders()
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
