package com.dip.material.ui.substitute

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.SubstituteDetailItem
import com.dip.material.data.models.SubstituteOrderItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SubstituteUiState(
    val orders: List<SubstituteOrderItem> = emptyList(),
    val selectedOrder: SubstituteOrderItem? = null,
    val details: List<SubstituteDetailItem> = emptyList(),
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val viewMode: ViewMode = ViewMode.ORDER_LIST
) {
    enum class ViewMode { ORDER_LIST, ORDER_DETAIL }
}

class SubstituteViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(SubstituteUiState())
    val state: StateFlow<SubstituteUiState> = _state.asStateFlow()

    init { viewModelScope.launch { loadOrders() } }

    private suspend fun loadOrders() {
        _state.update { it.copy(isLoading = true) }
        repo.getSubstituteOrders().fold(
            onSuccess = { res -> _state.update { it.copy(orders = res.data?.items ?: emptyList(), isLoading = false) } },
            onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
        )
    }

    /** 选择替代单 → 加载明细 */
    fun selectOrder(order: SubstituteOrderItem) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, selectedOrder = order) }
            repo.getSubstituteOrderDetails(order.id).fold(
                onSuccess = { res ->
                    _state.update { it.copy(isLoading = false, details = res.data?.details ?: emptyList(),
                        viewMode = SubstituteUiState.ViewMode.ORDER_DETAIL) }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 确认单个替代明细 */
    fun confirmDetail(orderId: Int, detailId: Int) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmSubstituteDetail(orderId, detailId).fold(
                onSuccess = {
                    ScanSoundManager.playSuccess()
                    // 刷新明细
                    repo.getSubstituteOrderDetails(orderId).fold(
                        onSuccess = { res ->
                            _state.update { it.copy(isLoading = false, details = res.data?.details ?: emptyList(),
                                scanMsg = "已确认一项") }
                        },
                        onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
                    )
                },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 确认全部 */
    fun confirmAll(orderId: Int) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmSubstituteAll(orderId).fold(
                onSuccess = {
                    ScanSoundManager.playSuccess()
                    _state.update { it.copy(isLoading = false, scanMsg = "全部确认完成") }
                    backToList()
                },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 返回列表 */
    fun backToList() {
        _state.update { it.copy(selectedOrder = null, details = emptyList(),
            viewMode = SubstituteUiState.ViewMode.ORDER_LIST) }
        viewModelScope.launch { loadOrders() }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
