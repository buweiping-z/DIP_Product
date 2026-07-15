package com.dip.material.ui.return_

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

data class ReturnUiState(
    val step: Int = 1,                        // 1=扫料号找库位 2=扫库位 3=逐袋扫退料 4=确认
    val scannedPart: PartItem? = null,
    val partLocations: List<InventoryAvailable> = emptyList(),
    val scannedLocation: LocationItem? = null,
    val scannedCounts: Map<Int, Int> = emptyMap(),  // partId → 已扫数量
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false
)

class ReturnViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(ReturnUiState())
    val state: StateFlow<ReturnUiState> = _state.asStateFlow()

    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    /** 步骤1：扫料号 → 查API → 获取当前库存库位 */
    fun scanPart(barcode: String) {
        val partNo = parsePartNo(barcode)
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.searchParts(partNo).fold(
                onSuccess = { res ->
                    val items = res.data?.items ?: emptyList()
                    if (items.isEmpty()) {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = "未找到部品: $partNo", scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    } else {
                        val part = items.first()
                        repo.getAvailableInventory(part.id).fold(
                            onSuccess = { invRes ->
                                ScanSoundManager.playSuccess()
                                _state.update { it.copy(scannedPart = part, partLocations = invRes.data ?: emptyList(), isLoading = false, step = 2, scanMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
                            },
                            onFailure = { e ->
                                ScanSoundManager.playError()
                                _state.update { it.copy(isLoading = false, scanMsg = e.message, scanEventId = it.scanEventId + 1, lastScanOk = false) }
                            }
                        )
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message, scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    /** 步骤2：扫库位 → 匹配校验 */
    fun scanLocation(code: String) {
        val trimmed = code.trim()
        val partLocations = _state.value.partLocations

        // 先匹配已有库存的库位
        val matched = partLocations.firstOrNull {
            it.locationCode.trim().equals(trimmed, ignoreCase = true)
        }
        if (matched != null) {
            ScanSoundManager.playSuccess()
            val loc = LocationItem(id = matched.locationId, locationCode = matched.locationCode)
            _state.update { it.copy(scannedLocation = loc, step = 3, scanMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
            return
        }

        // 库存中没匹配到 → 查全部库位
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.searchLocations(trimmed).fold(
                onSuccess = { res ->
                    val items = res.data?.items ?: emptyList()
                    val loc = items.firstOrNull { it.locationCode.trim().equals(trimmed, ignoreCase = true) }
                    if (loc != null) {
                        ScanSoundManager.playSuccess()
                        _state.update { it.copy(scannedLocation = loc, isLoading = false, step = 3, scanMsg = null, scanEventId = it.scanEventId + 1, lastScanOk = true) }
                    } else {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = "库位编号不存在: $trimmed", scanEventId = it.scanEventId + 1, lastScanOk = false) }
                    }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message, scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    /** 步骤3：扫袋子条码 → 匹配料号 → 本地计数+1 */
    fun scanBag(barcode: String) {
        val trimmed = barcode.trim()
        if (trimmed.length <= 14) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "无效料号(${trimmed.length}位)，需>14位", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        val bagPartNo = parsePartNo(trimmed)
        val part = _state.value.scannedPart ?: return
        if (!bagPartNo.equals(part.partNo, ignoreCase = true) &&
            !bagPartNo.equals(part.partNo.trim(), ignoreCase = true)) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "料号不匹配: $bagPartNo ≠ ${part.partNo}", scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        ScanSoundManager.playSuccess()
        val newCounts = _state.value.scannedCounts.toMutableMap()
        val pid = part.id
        newCounts[pid] = (newCounts[pid] ?: 0) + 1
        _state.update { it.copy(scannedCounts = newCounts,
            scanMsg = "已退: ${part.partNo} 第${newCounts[pid]}件", scanEventId = it.scanEventId + 1, lastScanOk = true) }
    }

    fun startBags() { _state.update { it.copy(step = 3) } }

    /** 提交退料完成 */
    fun finish() {
        val s = _state.value
        val locId = s.scannedLocation?.id ?: return
        val part = s.scannedPart ?: return
        val totalQty = s.scannedCounts[part.id] ?: 0
        if (totalQty <= 0) {
            _state.update { it.copy(scanMsg = "请至少扫描一件退料") }
            return
        }

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            val items = listOf(mapOf<String, Any?>(
                "part_id" to part.id,
                "part_no" to part.partNo,
                "quantity" to totalQty.toDouble()
            ))
            repo.batchFinishReturn(locId, items).fold(
                onSuccess = {
                    ScanSoundManager.playSuccess()
                    _state.update { it.copy(isLoading = false, step = 1, scannedPart = null,
                        partLocations = emptyList(), scannedLocation = null, scannedCounts = emptyMap(),
                        scanMsg = "退料完成: ${part.partNo} × ${totalQty}件") }
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message) }
                }
            )
        }
    }

    fun reset() { _state.value = ReturnUiState() }
}
