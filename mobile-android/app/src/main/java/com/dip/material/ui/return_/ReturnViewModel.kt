package com.dip.material.ui.return_

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.LocationItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ReturnUiState(
    val scannedBarcode: String = "",
    val locations: List<LocationItem> = emptyList(),
    val selectedLocationId: Int = 0,
    val isLoading: Boolean = false,
    val scanMsg: String? = null
)

class ReturnViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(ReturnUiState())
    val state: StateFlow<ReturnUiState> = _state.asStateFlow()

    fun loadLocations() {
        viewModelScope.launch {
            repo.searchLocations("").fold(
                onSuccess = { res -> _state.update { it.copy(locations = res.data?.items ?: emptyList()) } },
                onFailure = {}
            )
        }
    }

    fun scanReturn(barcode: String) {
        val locId = _state.value.selectedLocationId
        if (locId <= 0) { _state.update { it.copy(scanMsg = "请先选择库位") }; return }
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.scanReturn(barcode, locId).fold(
                onSuccess = { ScanSoundManager.playSuccess(); _state.update { it.copy(isLoading = false, scanMsg = "退料成功: $barcode", scannedBarcode = "") } },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    fun setBarcode(barcode: String) { _state.update { it.copy(scannedBarcode = barcode) } }
    fun setLocationId(id: Int) { _state.update { it.copy(selectedLocationId = id) } }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
