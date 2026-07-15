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
                    val allDetails = mutableListOf<PrepDetailItem>()
                    for (p in prepOrders) {
                        repo.getPrepDetail(p.id).fold(
                            onSuccess = { pr -> pr.data?.details?.let { allDetails.addAll(it) } },
                            onFailure = {}
                        )
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

        // 料号必须 >14 位
        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "无效料号(${trimmed.length}位)，需>14位") }
            return
        }

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
    }

    fun clearSelection() {
        val s = _state.value
        val initialCounts = s.initialScannedCounts

        // 只计算本次新扫的增量（当前累计 - 进入时已有），避免重复提交历史已确认数
        val newScans = s.scannedCounts
            .filter { (detailId, count) -> count > (initialCounts[detailId] ?: 0) }
            .mapValues { (detailId, count) -> count - (initialCounts[detailId] ?: 0) }

        _state.update { it.copy(selectedOrder = null, details = emptyList(), allDone = false,
            scannedCounts = emptyMap(), initialScannedCounts = emptyMap()) }

        // 先提交本次新增的确认 → 等后台处理完（最后一条会把订单改为已完成）
        // → 再刷新订单列表，确保已完成订单不再显示
        viewModelScope.launch {
            var hasError = false
            if (newScans.isNotEmpty()) {
                for ((detailId, count) in newScans) {
                    val res = repo.confirmOnline(detailId = detailId.toLong(), barcode = "", quantity = count.toDouble())
                    if (res.isFailure) {
                        _state.update { it.copy(scanMsg = "提交失败: ${res.exceptionOrNull()?.message}") }
                        hasError = true
                        break
                    }
                }
            }
            if (!hasError) loadOrders()
        }
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
