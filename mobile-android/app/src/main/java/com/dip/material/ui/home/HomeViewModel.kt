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
    val stats: DashboardStats? = null, val isLoading: Boolean = false
)

class HomeViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(HomeUiState())
    val state: StateFlow<HomeUiState> = _state.asStateFlow()

    init { loadStats() }

    fun loadStats() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getDashboardStats().fold(
                onSuccess = { res -> _state.update { it.copy(stats = res.data, isLoading = false) } },
                onFailure = { e -> _state.update { it.copy(isLoading = false) } }
            )
        }
    }
}
