package com.dip.material.ui.online

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.OrderItem
import com.dip.material.data.models.PrepDetailItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
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
    val allDone: Boolean = false,
    val scannedCounts: Map<Int, Int> = emptyMap(),  // prepDetailId → 累计已确认数（含历史+本次）
    val initialScannedCounts: Map<Int, Int> = emptyMap()  // 进入时的已确认数（用于计算增量，退出时只提交增量）
)

class OnlineViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(OnlineUiState())
    val state: StateFlow<OnlineUiState> = _state.asStateFlow()

    init { viewModelScope.launch { loadOrders() } }

    /** 解析料号：≤14位取全部，>14位去掉末尾4位 */
    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    private suspend fun loadOrders() {
        _state.update { it.copy(isLoading = true) }
        repo.getOrders(status = 2).fold(
            onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
            onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
        )
    }

    /** 选订单 → 加载备料明细 + 已上线消耗量 */
    fun selectOrder(order: OrderItem) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, selectedOrder = order, allDone = false, scannedCounts = emptyMap(), initialScannedCounts = emptyMap()) }
            repo.getOrderDetail(order.id).fold(
                onSuccess = { res ->
                    val prepOrders = res.data?.prepOrders ?: emptyList()
                    if (prepOrders.isEmpty()) {
                        _state.update { it.copy(isLoading = false, scanMsg = "该订单无备料单") }
                        return@fold
                    }
                    // 并行加载所有备料单明细
                    val detailsResults = prepOrders.map { p ->
                        async { repo.getPrepDetail(p.id) }
                    }.awaitAll()
                    val allDetails = detailsResults.flatMap { r ->
                        r.getOrNull()?.data?.details ?: emptyList()
                    }
                    // 从后端 online_consumed_qty 恢复历史已确认数，退出再进不需要重新扫描
                    val initialCounts = mutableMapOf<Int, Int>()
                    for (d in allDetails) {
                        val consumed = d.onlineConsumedQty.toInt()
                        if (consumed > 0) initialCounts[d.id] = consumed
                    }
                    _state.update { it.copy(isLoading = false, details = allDetails,
                        scannedCounts = initialCounts, initialScannedCounts = initialCounts.toMap()) }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    fun scanOnline(barcode: String) {
        val trimmed = barcode.trim()

        val partNo = parsePartNo(trimmed)
        val s = _state.value

        // 用解析后的料号匹配
        val match = s.details.firstOrNull {
            it.partNo.trim().equals(partNo, ignoreCase = true)
        }
        if (match == null) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "未匹配到料号: $partNo") }
            return
        }

        // 本地累加计数
        ScanSoundManager.playSuccess()
        val newCounts = s.scannedCounts.toMutableMap()
        newCounts[match.id] = (newCounts[match.id] ?: 0) + 1

        // 全部明细都被确认过至少一次 → allDone
        val allDone = s.details.isNotEmpty() && s.details.all { d ->
            (newCounts[d.id] ?: 0) > 0
        }

        _state.update { it.copy(scannedCounts = newCounts, scanMsg = "已确认: $partNo", allDone = allDone) }

        // 立即提交服务端持久化
        viewModelScope.launch {
            repo.confirmOnline(detailId = match.id.toLong(), barcode = trimmed, quantity = 1.0)
                .onFailure { e -> _state.update { it.copy(scanMsg = "提交失败: ${e.message}") } }
        }
    }

    fun clearSelection() {
        _state.update { it.copy(selectedOrder = null, details = emptyList(), allDone = false,
            scannedCounts = emptyMap(), initialScannedCounts = emptyMap()) }
        viewModelScope.launch { loadOrders() }
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
