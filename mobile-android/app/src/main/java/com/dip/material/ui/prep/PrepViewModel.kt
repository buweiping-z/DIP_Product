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
    val allDone: Boolean = false
)

class PrepViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(PrepUiState())
    val state: StateFlow<PrepUiState> = _state.asStateFlow()

    init { loadOrders() }

    fun loadOrders() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getPrepOrders(status = 1).fold(
                onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
                onFailure = { e -> _state.update { it.copy(isLoading = false) } }
            )
        }
    }

    fun selectOrder(prepId: Int) {
        viewModelScope.launch {
            repo.getPrepDetail(prepId).fold(
                onSuccess = { res -> _state.update { it.copy(selectedOrder = res.data, allDone = false) } },
                onFailure = {}
            )
        }
    }

    fun scanItem(barcode: String) {
        val prepId = _state.value.selectedOrder?.id ?: return
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }
            repo.scanPrepItem(prepId, barcode).fold(
                onSuccess = { res ->
                    // res 是 ApiResponse 的 data 部分（Map 结构: code, data, message）
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
                            val partNo = data["part_no"] as? String ?: ""
                            val allDone = data["all_done"] as? Boolean ?: false
                            _state.update { it.copy(isLoading = false, scanMsg = "已备齐: $partNo", allDone = allDone, scanEventId = it.scanEventId + 1, lastScanOk = true) }
                            selectOrder(prepId)
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
        _state.update { it.copy(selectedOrder = null, allDone = false) }
        loadOrders()
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
