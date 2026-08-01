package com.dip.material.ui.home

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class HomeUiState(
    val isLoading: Boolean = false,
    val pendingPrep: Int = 0,
    val pendingRefill: Int = 0,
    val pendingSubstitute: Int = 0,
    val pendingOnline: Int = 0,
    val pendingOutbound: Int = 0,
    val pendingChangeover: Int = 0,
    val pendingCallMaterial: Int = 0
)

class HomeViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    // 首次加载由 HomeScreen LaunchedEffect 触发，不在 init 中预加载（避免重复）

    fun loadPendingTasks() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }

            repo.getMobileCounts().fold(
                onSuccess = { res ->
                    val d = res.data
                    _state.update { it.copy(isLoading = false,
                        pendingPrep = d?.get("prep") ?: 0,
                        pendingRefill = d?.get("refill") ?: 0,
                        pendingSubstitute = d?.get("substitute") ?: 0,
                        pendingOnline = d?.get("online") ?: 0,
                        pendingOutbound = d?.get("outbound") ?: 0,
                        pendingChangeover = d?.get("changeover") ?: 0,
                        pendingCallMaterial = d?.get("call_material") ?: 0) }
                },
                onFailure = { _state.update { it.copy(isLoading = false) } }
            )
        }
    }
}
