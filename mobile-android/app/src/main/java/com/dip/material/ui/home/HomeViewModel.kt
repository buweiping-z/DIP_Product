package com.dip.material.ui.home

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.async
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
    val pendingChangeover: Int = 0
)

class HomeViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    // 首次加载由 HomeScreen LaunchedEffect 触发，不在 init 中预加载（避免重复）

    fun loadPendingTasks() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }

            // 6 个 API 并行请求，不互相等待
            val prep = async { repo.getPrepOrders(status = 1).getOrNull()?.data?.total ?: 0 }
            val refill = async { repo.getActiveRefillBatches().getOrNull()?.data?.size ?: 0 }
            val sub = async { repo.getSubstituteOrders(status = 1).getOrNull()?.data?.total ?: 0 }
            val online = async { repo.getOrders(status = 2).getOrNull()?.data?.total ?: 0 }
            val outbound = async { repo.getOutboundOrders(status = 1).getOrNull()?.data?.total ?: 0 }
            val changeover = async { repo.getChangeoverBatches().getOrNull()?.size ?: 0 }

            val prepCount = prep.await()
            val refillCount = refill.await()
            val subCount = sub.await()
            val onlineCount = online.await()
            val outboundCount = outbound.await()
            val changeoverCount = changeover.await()

            _state.update { it.copy(isLoading = false,
                pendingPrep = prepCount, pendingRefill = refillCount, pendingSubstitute = subCount,
                pendingOnline = onlineCount, pendingOutbound = outboundCount,
                pendingChangeover = changeoverCount) }
        }
    }
}
