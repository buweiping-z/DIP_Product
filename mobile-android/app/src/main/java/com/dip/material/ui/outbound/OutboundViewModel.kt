package com.dip.material.ui.outbound

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.OutboundOrderItem
import com.dip.material.data.models.OutboundOrderDetail
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class OutboundUiState(
    val orders: List<OutboundOrderItem> = emptyList(),
    val selectedOrder: OutboundOrderDetail? = null,
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false,
    val allDone: Boolean = false,
    val scannedCounts: Map<Int, Int> = emptyMap()  // detailId → 已扫袋数
) {
    val totalParts: Int get() = selectedOrder?.details?.size ?: 0
    val doneParts: Int get() = selectedOrder?.details?.count { d ->
        (scannedCounts[d.id] ?: 0) > 0
    } ?: 0
}

class OutboundViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(OutboundUiState())
    val state: StateFlow<OutboundUiState> = _state.asStateFlow()

    init { viewModelScope.launch { loadOrders() } }

    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

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
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getOutboundOrderDetail(order.id).fold(
                onSuccess = { res ->
                    if (res.code == 0 && res.data != null) {
                        _state.update { it.copy(selectedOrder = res.data, scannedCounts = emptyMap(),
                            allDone = false, isLoading = false) }
                    }
                },
                onFailure = { _state.update { it.copy(isLoading = false) } }
            )
        }
    }

    /** 扫码核对：匹配料号 → 本地计数器+1，不调后端 */
    fun scanOutbound(barcode: String) {
        val trimmed = barcode.trim()
        val details = _state.value.selectedOrder?.details ?: return

        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "无效料号(${trimmed.length}位)，需>14位", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        val partNo = parsePartNo(trimmed)
        val matched = details.firstOrNull { d ->
            d.partNo.trim().equals(partNo, ignoreCase = true)
        }

        if (matched == null) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "料号不匹配: $partNo", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        ScanSoundManager.playSuccess()
        val newCounts = _state.value.scannedCounts.toMutableMap()
        newCounts[matched.id] = (newCounts[matched.id] ?: 0) + 1
        val totalScanned = newCounts[matched.id]!!
        val allDone = details.all { (newCounts[it.id] ?: 0) > 0 }
        _state.update { it.copy(
            scannedCounts = newCounts, allDone = allDone,
            scanMsg = "已确认: ${matched.partNo} 第${totalScanned}袋",
            scanEventId = it.scanEventId + 1, lastScanOk = true) }
    }

    /** 全部扫描完成后提交：逐明细调确认接口 */
    fun confirmAll() {
        val order = _state.value.selectedOrder ?: return
        val counts = _state.value.scannedCounts
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }
            try {
                for (detail in order.details) {
                    val cnt = counts[detail.id] ?: 0
                    if (cnt > 0) {
                        // 逐明细确认
                        val res = repo.confirmOutboundDetail(order.id, detail.id, detail.partNo)
                        if (res.isFailure) {
                            _state.update { it.copy(isLoading = false,
                                scanMsg = "核销失败: ${detail.partNo} - ${res.exceptionOrNull()?.message}") }
                            return@launch
                        }
                    }
                }
                // 整单完成
                repo.confirmOutboundAll(order.id).fold(
                    onSuccess = {
                        ScanSoundManager.playSuccess()
                        _state.update { it.copy(selectedOrder = null, isLoading = false, allDone = false) }
                        loadOrders()
                    },
                    onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
                )
            } catch (e: Exception) {
                _state.update { it.copy(isLoading = false, scanMsg = e.message) }
            }
        }
    }

    fun clearSelection() { _state.update { it.copy(selectedOrder = null, allDone = false) }; loadOrders() }
}
