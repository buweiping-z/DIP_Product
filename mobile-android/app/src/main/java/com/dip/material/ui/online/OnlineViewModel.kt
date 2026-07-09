package com.dip.material.ui.online

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class OnlineUiState(
    val isLoading: Boolean = false,
    val scanMsg: String? = null
)

class OnlineViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(OnlineUiState())
    val state: StateFlow<OnlineUiState> = _state.asStateFlow()

    fun confirmOnline(prepOrderId: Int, partNo: String, barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.confirmOnline(prepOrderId, partNo, barcode).fold(
                onSuccess = { _state.update { it.copy(isLoading = false, scanMsg = "上线确认成功") } },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
