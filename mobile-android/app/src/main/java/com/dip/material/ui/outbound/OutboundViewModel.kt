package com.dip.material.ui.outbound

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.OutboundOrderItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class OutboundUiState(
    val orders: List<OutboundOrderItem> = emptyList(),
    val selectedOrder: OutboundOrderItem? = null,
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val allDone: Boolean = false
)

class OutboundViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(OutboundUiState())
    val state: StateFlow<OutboundUiState> = _state.asStateFlow()

    init { loadOrders() }

    fun loadOrders() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getOutboundOrders(status = 1).fold(
                onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    fun selectOrder(order: OutboundOrderItem) {
        _state.update { it.copy(selectedOrder = order, allDone = false, scanMsg = null) }
    }

    fun scanOutbound(barcode: String) {
        val order = _state.value.selectedOrder ?: return
        val trimmed = barcode.trim()

        // 大小写不敏感 + 条码包含料号即匹配（与备料一致）
        val partNo = order.partNo.trim()
        if (!trimmed.equals(partNo, ignoreCase = true) && !trimmed.contains(partNo, ignoreCase = true)) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "条码与出库料号不匹配: $trimmed") }
            return
        }

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }
            repo.confirmOutbound(order.id, trimmed).fold(
                onSuccess = { res ->
                    if (res.code == 0) {
                        ScanSoundManager.playSuccess()
                        _state.update { it.copy(isLoading = false, allDone = true,
                            scanMsg = "出库核销成功: ${order.partNo} × ${order.quantity.toInt()}") }
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res.message ?: "核销失败") }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message) }
                }
            )
        }
    }

    fun clearSelection() { _state.update { it.copy(selectedOrder = null, allDone = false) }; loadOrders() }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
