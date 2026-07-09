package com.dip.material.ui.refill

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.PendingItem
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class RefillUiState(
    val pendingItems: List<PendingItem> = emptyList(),
    val isLoading: Boolean = false,
    val scanMsg: String? = null
)

class RefillViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(RefillUiState())
    val state: StateFlow<RefillUiState> = _state.asStateFlow()

    init { loadPending() }

    fun loadPending() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getPendingItems().fold(
                onSuccess = { res -> _state.update { it.copy(pendingItems = res.data ?: emptyList(), isLoading = false) } },
                onFailure = { e -> _state.update { it.copy(isLoading = false) } }
            )
        }
    }

    fun scanRefill(barcode: String, prepOrderId: Int, detailId: Int) {
        viewModelScope.launch {
            repo.scanPrepItem(prepOrderId, barcode, detailId).fold(
                onSuccess = { _state.update { it.copy(scanMsg = "补料成功: $barcode") }; loadPending() },
                onFailure = { e -> _state.update { it.copy(scanMsg = e.message) } }
            )
        }
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
