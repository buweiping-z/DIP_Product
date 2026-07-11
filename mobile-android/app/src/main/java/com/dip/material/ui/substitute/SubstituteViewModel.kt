package com.dip.material.ui.substitute

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SubstituteUiState(
    val isLoading: Boolean = false,
    val scanMsg: String? = null
)

class SubstituteViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(SubstituteUiState())
    val state: StateFlow<SubstituteUiState> = _state.asStateFlow()

    fun createSubstitute(originalPartId: Int, substitutePartId: Int, sourceLocationId: Int, targetLocationId: Int, quantity: Double) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.createSubstitute(originalPartId, substitutePartId, sourceLocationId, targetLocationId, quantity).fold(
                onSuccess = { ScanSoundManager.playSuccess(); _state.update { it.copy(isLoading = false, scanMsg = "移库记录已创建") } },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
