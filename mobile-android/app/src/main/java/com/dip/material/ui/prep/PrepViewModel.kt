package com.dip.material.ui.prep

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.PrepOrderItem
import com.dip.material.data.models.PrepDetail
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class PrepUiState(
    val orders: List<PrepOrderItem> = emptyList(),
    val selectedOrder: PrepDetail? = null,
    val scanMsg: String? = null,
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
                    val data = res.data
                    if (data is Map<*, *>) {
                        val matched = data["matched"] as? Boolean ?: false
                        if (matched) {
                            val partNo = data["part_no"] as? String ?: ""
                            val allDone = data["all_done"] as? Boolean ?: false
                            _state.update { it.copy(isLoading = false, scanMsg = "已备齐: $partNo", allDone = allDone) }
                            if (allDone) {
                                // 全部完成，刷新详情
                                selectOrder(prepId)
                            } else {
                                // 刷新详情
                                selectOrder(prepId)
                            }
                        } else {
                            val msg = data["message"] as? String ?: "未匹配"
                            _state.update { it.copy(isLoading = false, scanMsg = msg) }
                        }
                    }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    fun clearSelection() { _state.update { it.copy(selectedOrder = null) } }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
