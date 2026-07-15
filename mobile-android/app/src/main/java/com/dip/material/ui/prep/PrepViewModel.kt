package com.dip.material.ui.prep

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.PrepOrderItem
import com.dip.material.data.models.PrepDetail
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class PrepUiState(
    val orders: List<PrepOrderItem> = emptyList(),
    val selectedOrder: PrepDetail? = null,
    val scanMsg: String? = null,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false,
    val isLoading: Boolean = false,
    val allDone: Boolean = false,
    val scannedCounts: Map<Int, Int> = emptyMap()  // prepDetailId → 本次已扫袋数（手机端自维护，从0开始）
)

class PrepViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(PrepUiState())
    val state: StateFlow<PrepUiState> = _state.asStateFlow()

    init { viewModelScope.launch { loadOrders() } }

    /** 解析料号：≤14位取全部，>14位去掉末尾4位 */
    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    private suspend fun loadOrders() {
        _state.update { it.copy(isLoading = true) }
        repo.getPrepOrders(status = 1).fold(
            onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
            onFailure = { e -> _state.update { it.copy(isLoading = false) } }
        )
    }

    fun selectOrder(prepId: Int) {
        viewModelScope.launch {
            repo.getPrepDetail(prepId).fold(
                onSuccess = { res -> _state.update { it.copy(selectedOrder = res.data, allDone = false, scannedCounts = emptyMap()) } },
                onFailure = {}
            )
        }
    }

    fun scanItem(barcode: String) {
        val trimmed = barcode.trim()
        val prepId = _state.value.selectedOrder?.id ?: return

        // 料号必须 >14 位
        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "无效料号(${trimmed.length}位)，需>14位", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        val partNo = parsePartNo(trimmed)

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }
            repo.scanPrepItem(prepId, partNo).fold(
                onSuccess = { res ->
                    val data = (res["data"] as? Map<*, *>)
                    val code = (res["code"] as? Double)?.toInt() ?: -1
                    if (code != 0 || data == null) {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res["message"] as? String ?: "请求失败", scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    } else {
                        val matched = data["matched"] as? Boolean ?: false
                        if (!matched) {
                            ScanSoundManager.playError()
                            _state.update { it.copy(isLoading = false, scanMsg = data["message"] as? String ?: "未匹配到备料明细", scanEventId = it.scanEventId + 1, lastScanOk = false) }
                        } else {
                            ScanSoundManager.playSuccess()
                            val detailId = (data["prep_detail_id"] as? Double)?.toInt()
                            val matchedPartNo = data["part_no"] as? String ?: partNo

                            // 本地累加计数（从0开始，不依赖后端ActualQty）
                            val newCounts = _state.value.scannedCounts.toMutableMap()
                            if (detailId != null) {
                                newCounts[detailId] = (newCounts[detailId] ?: 0) + 1
                            }

                            // 全部明细都被扫过至少一次 → allDone
                            val allDetails = _state.value.selectedOrder?.details ?: emptyList()
                            val allDone = allDetails.isNotEmpty() && allDetails.all { d ->
                                d.status == 2 || (newCounts[d.id] ?: 0) > 0
                            }

                            _state.update { it.copy(isLoading = false, scannedCounts = newCounts, scanMsg = "已备: $matchedPartNo", allDone = allDone, scanEventId = it.scanEventId + 1, lastScanOk = true) }
                        }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message, scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    fun clearSelection() {
        val prepId = _state.value.selectedOrder?.id
        val scannedIds = _state.value.scannedCounts.filter { it.value > 0 }.keys.toList()
        _state.update { it.copy(selectedOrder = null, allDone = false, scannedCounts = emptyMap()) }
        // 先等 finishPrep 完成（后端标记 status=2），再刷新列表
        if (prepId != null && scannedIds.isNotEmpty()) {
            viewModelScope.launch {
                repo.finishPrep(prepId, scannedIds)
                loadOrders()
            }
        } else {
            viewModelScope.launch { loadOrders() }
        }
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
