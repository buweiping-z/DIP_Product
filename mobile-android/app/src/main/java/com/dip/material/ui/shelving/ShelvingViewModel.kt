package com.dip.material.ui.shelving

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.InventoryAvailable
import com.dip.material.data.models.LocationItem
import com.dip.material.data.models.PartItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
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
    val resultMsg: String? = null,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false
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
                    if (items.isEmpty()) {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, resultMsg = "未找到部品: $barcode", scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    } else {
                        val part = items.first()
                        repo.getAvailableInventory(part.id).fold(
                            onSuccess = { invRes ->
                                ScanSoundManager.playSuccess()
                                _state.update { it.copy(scannedPart = part, partLocations = invRes.data ?: emptyList(), isLoading = false, step = 2, resultMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
                            },
                            onFailure = { e ->
                                ScanSoundManager.playError()
                                _state.update { it.copy(isLoading = false, resultMsg = e.message, scanEventId = it.scanEventId + 1, lastScanOk = false) }
                            }
                        )
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, resultMsg = e.message, scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    fun lookupLocation(code: String) {
        val stateVal = _state.value
        val partLocations = stateVal.partLocations
        val trimmed = code.trim()

        // 直接在步骤1已加载的部品库存库位列表中精确匹配（去空格 + 不区分大小写）
        val matched = partLocations.firstOrNull {
            it.locationCode.trim().equals(trimmed, ignoreCase = true)
        }
        if (matched != null) {
            ScanSoundManager.playSuccess()
            val loc = com.dip.material.data.models.LocationItem(id = matched.locationId, locationCode = matched.locationCode)
            _state.update { it.copy(scannedLocation = loc, step = 3, resultMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
        } else {
            ScanSoundManager.playError()
            _state.update { it.copy(resultMsg = "库位编号错误: $trimmed（不在部品库存列表中）", scanEventId = it.scanEventId + 1, lastScanOk = false) }
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
