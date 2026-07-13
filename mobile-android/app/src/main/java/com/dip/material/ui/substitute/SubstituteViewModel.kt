package com.dip.material.ui.substitute

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import com.dip.material.data.models.SubstituteOrderItem
import com.dip.material.data.models.SubstituteOrderDetail
import com.dip.material.data.models.SubstituteDetailItem
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SubstituteUiState(
    val orders: List<SubstituteOrderItem> = emptyList(),
    val selectedOrder: SubstituteOrderDetail? = null,
    val scanMsg: String? = null,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false,
    val isLoading: Boolean = false,
    val allDone: Boolean = false,
    // 当前匹配结果（扫码后到确认前）
    val matchedDetail: SubstituteDetailItem? = null,
    // 匹配到多条时的候选列表
    val matchCandidates: List<SubstituteDetailItem> = emptyList(),
    val showCandidates: Boolean = false
) {
    // 实时计算已确认数和总数
    val confirmedCount: Int get() = selectedOrder?.details?.count { it.status == 2 } ?: 0
    val totalCount: Int get() = selectedOrder?.totalCount ?: 0
}

class SubstituteViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(SubstituteUiState())
    val state: StateFlow<SubstituteUiState> = _state.asStateFlow()

    init { viewModelScope.launch { loadOrders() } }

    /** 解析料号：≤14位取全部，>14位去掉末尾4位 */
    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    private suspend fun loadOrders() {
        _state.update { it.copy(isLoading = true) }
        repo.getSubstituteOrders(status = 1).fold(
            onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
            onFailure = { _state.update { it.copy(isLoading = false) } }
        )
    }

    fun selectOrder(orderId: Int) {
        viewModelScope.launch {
            repo.getSubstituteOrderDetails(orderId).fold(
                onSuccess = { res ->
                    if (res.code == 0 && res.data != null) {
                        val done = res.data.details.all { it.status == 2 }
                        _state.update { it.copy(selectedOrder = res.data, allDone = done,
                            matchedDetail = null, matchCandidates = emptyList(), showCandidates = false) }
                    }
                },
                onFailure = {}
            )
        }
    }

    fun scanBarcode(barcode: String) {
        val trimmed = barcode.trim()
        val detailList = _state.value.selectedOrder?.details ?: return

        // 条码须>14位
        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "无效料号(${trimmed.length}位)，需>14位", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        val partNo = parsePartNo(trimmed)

        // 在未确认明细中匹配替代料号
        val matches = detailList.filter {
            it.status == 1 && it.substitutePartNo.trim().equals(partNo, ignoreCase = true)
        }

        when {
            matches.isEmpty() -> {
                ScanSoundManager.playError()
                _state.update { it.copy(
                    scanMsg = "无匹配明细 (扫码料号: $partNo)", matchedDetail = null,
                    matchCandidates = emptyList(), showCandidates = false,
                    scanEventId = it.scanEventId + 1, lastScanOk = false) }
            }
            matches.size == 1 -> {
                // 唯一匹配，自动选中
                val m = matches.first()
                ScanSoundManager.playSuccess()
                _state.update { it.copy(
                    matchedDetail = m, matchCandidates = emptyList(), showCandidates = false,
                    scanMsg = "匹配: ${m.substitutePartNo} ← ${m.originalPartNo}",
                    scanEventId = it.scanEventId + 1, lastScanOk = true) }
            }
            else -> {
                // 多条匹配，弹出选择列表
                ScanSoundManager.playSuccess()
                _state.update { it.copy(
                    matchedDetail = null, matchCandidates = matches, showCandidates = true,
                    scanMsg = "找到 ${matches.size} 条匹配，请选择",
                    scanEventId = it.scanEventId + 1, lastScanOk = true) }
            }
        }
    }

    fun selectCandidate(detail: SubstituteDetailItem) {
        _state.update { it.copy(
            matchedDetail = detail, matchCandidates = emptyList(), showCandidates = false,
            scanMsg = "匹配: ${detail.substitutePartNo} ← ${detail.originalPartNo}") }
    }

    fun cancelCurrentMatch() {
        _state.update { it.copy(matchedDetail = null, matchCandidates = emptyList(), showCandidates = false, scanMsg = null) }
    }

    fun confirmDetail() {
        val detail = _state.value.matchedDetail ?: return
        val orderId = _state.value.selectedOrder?.orderId ?: return

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmSubstituteDetail(orderId, detail.id).fold(
                onSuccess = { res ->
                    if (res.code == 0) {
                        ScanSoundManager.playSuccess()
                        // 更新本地明细状态
                        val updatedDetails = _state.value.selectedOrder?.details?.map {
                            if (it.id == detail.id) it.copy(status = 2) else it
                        } ?: emptyList()
                        val updatedOrder = _state.value.selectedOrder?.copy(details = updatedDetails)
                        val allDone = updatedDetails.all { it.status == 2 }
                        _state.update { it.copy(isLoading = false, selectedOrder = updatedOrder,
                            matchedDetail = null, allDone = allDone,
                            scanMsg = "✓ 确认成功: ${detail.substitutePartNo}",
                            scanEventId = it.scanEventId + 1, lastScanOk = true) }
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res.message ?: "确认失败",
                            scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message,
                        scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    fun confirmAll() {
        val orderId = _state.value.selectedOrder?.orderId ?: return

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmSubstituteAll(orderId).fold(
                onSuccess = { res ->
                    if (res.code == 0) {
                        ScanSoundManager.playSuccess()
                        _state.update { it.copy(isLoading = false,
                            scanMsg = "移库完成！",
                            scanEventId = it.scanEventId + 1, lastScanOk = true) }
                        // 短暂显示"移库完成"后自动退出到订单列表
                        kotlinx.coroutines.delay(800)
                        clearSelection()
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = res.message ?: "提交失败",
                            scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message,
                        scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    fun clearSelection() {
        _state.update { it.copy(selectedOrder = null, allDone = false,
            matchedDetail = null, matchCandidates = emptyList(), showCandidates = false, scanMsg = null) }
        viewModelScope.launch { loadOrders() }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
