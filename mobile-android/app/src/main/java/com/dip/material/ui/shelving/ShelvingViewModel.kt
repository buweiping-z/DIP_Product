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
    val rawBarcode: String = "",
    val scannedPart: PartItem? = null,
    val partLocations: List<InventoryAvailable> = emptyList(),
    val scannedLocation: LocationItem? = null,
    val bagCount: Int = 0,
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

    /** 解析料号：≤14位取全部，>14位去掉末尾4位 */
    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    /** 步骤1：扫部品 → 查API → 获取库存库位 */
    fun scanPart(barcode: String) {
        val partNo = parsePartNo(barcode)
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, rawBarcode = barcode) }
            repo.searchParts(partNo).fold(
                onSuccess = { res ->
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty()) {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, resultMsg = "未找到部品: $partNo", scanEventId = it.scanEventId + 1, lastScanOk = false) }
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

    /** 步骤2：扫库位 → 仅允许部品已有库存的库位，防错放 */
    fun scanLocation(code: String) {
        val stateVal = _state.value
        val partLocations = stateVal.partLocations
        val trimmed = code.trim()

        val matched = partLocations.firstOrNull {
            it.locationCode.trim().equals(trimmed, ignoreCase = true)
        }
        if (matched != null) {
            ScanSoundManager.playSuccess()
            val loc = LocationItem(id = matched.locationId, locationCode = matched.locationCode)
            _state.update { it.copy(scannedLocation = loc, step = 3, resultMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
            return
        }

        // 不在该部品的库存库位列表中 → 拒绝
        ScanSoundManager.playError()
        val available = partLocations.joinToString(", ") { it.locationCode }
        _state.update { it.copy(resultMsg = "库位不匹配，仅允许: $available", scanEventId = it.scanEventId + 1, lastScanOk = false) }
    }

    /** 步骤3：扫描袋子 → 料号必须>14位，解析后校验匹配 + 计数 */
    fun scanBag(barcode: String) {
        val trimmed = barcode.trim()

        // 袋子条码必须大于14位
        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(resultMsg = "无效袋条码(${trimmed.length}位)，需>14位", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        val bagPartNo = parsePartNo(trimmed)
        val stateVal = _state.value
        val part = stateVal.scannedPart ?: return

        // 校验料号是否与步骤1的部品匹配
        if (!bagPartNo.equals(part.partNo, ignoreCase = true) &&
            !bagPartNo.equals(part.partNo.trim(), ignoreCase = true)) {
            ScanSoundManager.playError()
            _state.update { it.copy(resultMsg = "料号不匹配: $bagPartNo ≠ ${part.partNo}", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        ScanSoundManager.playSuccess()
        _state.update { it.copy(bagCount = it.bagCount + 1, resultMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
    }

    fun setQuantity(qty: String) { _state.update { it.copy(quantity = qty) } }

    /** 步骤3→4：完成扫袋，进入数量输入 */
    fun finishBags() {
        if (_state.value.bagCount == 0) {
            _state.update { it.copy(resultMsg = "请至少扫描一袋") }
            return
        }
        _state.update { it.copy(step = 4) }
    }

    /** 步骤4：确认上传 */
    fun confirm() {
        val s = _state.value
        val part = s.scannedPart ?: return
        val loc = s.scannedLocation ?: return
        val qty = s.quantity.toDoubleOrNull() ?: return
        if (qty <= 0) {
            _state.update { it.copy(resultMsg = "请输入有效数量") }
            return
        }

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.directShelving(part.partNo, loc.locationCode, qty).fold(
                onSuccess = { res ->
                    if (res.code == 0)
                        _state.update { it.copy(isLoading = false, step = 1, rawBarcode = "", scannedPart = null, partLocations = emptyList(), scannedLocation = null, bagCount = 0, quantity = "", resultMsg = "上架成功! ${s.bagCount}袋 共${qty}个") }
                    else
                        _state.update { it.copy(isLoading = false, resultMsg = res.message ?: "失败") }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, resultMsg = e.message) } }
            )
        }
    }

    fun reset() { _state.value = ShelvingUiState() }
}
