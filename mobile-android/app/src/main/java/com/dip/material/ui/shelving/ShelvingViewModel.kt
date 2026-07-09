package com.dip.material.ui.shelving

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.InventoryAvailable
import com.dip.material.data.models.LocationItem
import com.dip.material.data.models.PartItem
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ShelvingUiState(
    val step: Int = 1,
    val scannedPart: PartItem? = null,
    val partLocations: List<InventoryAvailable> = emptyList(),
    val scannedLocation: LocationItem? = null,
    val quantity: String = "",
    val isLoading: Boolean = false,
    val resultMsg: String? = null
)

class ShelvingViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(ShelvingUiState())
    val state: StateFlow<ShelvingUiState> = _state.asStateFlow()

    fun lookupPart(barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.searchParts(barcode).fold(
                onSuccess = { res ->
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty())
                        _state.update { it.copy(isLoading = false, resultMsg = "未找到部品: $barcode") }
                    else {
                        val part = items.first()
                        repo.getAvailableInventory(part.id).fold(
                            onSuccess = { invRes ->
                                _state.update { it.copy(scannedPart = part, partLocations = invRes.data ?: emptyList(), isLoading = false, step = 2, resultMsg = null) }
                            },
                            onFailure = { e -> _state.update { it.copy(isLoading = false, resultMsg = e.message) } }
                        )
                    }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, resultMsg = e.message) } }
            )
        }
    }

    fun lookupLocation(code: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.searchLocations(code).fold(
                onSuccess = { res ->
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty())
                        _state.update { it.copy(isLoading = false, resultMsg = "未找到库位: $code") }
                    else
                        _state.update { it.copy(scannedLocation = items.first(), isLoading = false, step = 3, resultMsg = null) }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, resultMsg = e.message) } }
            )
        }
    }

    fun setQuantity(qty: String) { _state.update { it.copy(quantity = qty) } }
    fun gotoStep2() { _state.update { it.copy(step = 2) } }
    fun gotoStep3() { _state.update { it.copy(step = 3) } }

    fun confirm() {
        val s = _state.value; val part = s.scannedPart ?: return; val loc = s.scannedLocation ?: return
        val qty = s.quantity.toDoubleOrNull() ?: return
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.directShelving(part.partNo, loc.locationCode, qty).fold(
                onSuccess = { res ->
                    if (res.code == 0)
                        _state.update { it.copy(isLoading = false, step = 1, scannedPart = null, partLocations = emptyList(), scannedLocation = null, quantity = "", resultMsg = "上架成功!") }
                    else
                        _state.update { it.copy(isLoading = false, resultMsg = res.message ?: "失败") }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, resultMsg = e.message) } }
            )
        }
    }

    fun reset() { _state.value = ShelvingUiState() }
}
