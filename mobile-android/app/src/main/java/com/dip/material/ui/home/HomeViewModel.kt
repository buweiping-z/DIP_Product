package com.dip.material.ui.home

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.DashboardStats
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
    val pendingSubstitute: Int = 0
)

class HomeViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    init { loadPendingTasks() }

    fun loadPendingTasks() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            // 待备料单
            var prepCount = 0
            repo.getPrepOrders(status = 1).fold(
                onSuccess = { prepCount = it.data?.total ?: 0 },
                onFailure = {}
            )
            // 未完成补料批次
            var refillCount = 0
            repo.getActiveRefillBatches().fold(
                onSuccess = { refillCount = it.data?.size ?: 0 },
                onFailure = {}
            )
            // 待确认替代料移库
            var subCount = 0
            repo.getSubstituteOrders(status = 1).fold(
                onSuccess = { subCount = it.data?.total ?: 0 },
                onFailure = {}
            )
            _state.update { it.copy(isLoading = false,
                pendingPrep = prepCount, pendingRefill = refillCount, pendingSubstitute = subCount) }
        }
    }
}
