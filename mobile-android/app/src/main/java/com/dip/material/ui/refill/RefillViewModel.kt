package com.dip.material.ui.refill

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.PendingItem
import com.dip.material.data.models.RefillStartItem
import com.dip.material.data.repository.AppRepository
import com.dip.material.utils.ScanSoundManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class RefillUiState(
    val parts: List<PendingItem> = emptyList(),
    val selectedIds: Set<Int> = emptySet(),
    val pickedIds: Set<Int> = emptySet(),
    val verifiedIds: Set<Int> = emptySet(),
    val batchNo: String = "",
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val step: Int = 0,
    val boxPartNo: String = "",
    val boxPart: PendingItem? = null
)

class RefillViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(RefillUiState())
    val state: StateFlow<RefillUiState> = _state.asStateFlow()

    init { checkActiveBatches() }

    /** 进入补料时检查未完成批次，多个则显示列表供选择 */
    fun checkActiveBatches() {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getActiveRefillBatches().fold(
                onSuccess = { res ->
                    val batches = res.data ?: emptyList()
                    if (batches.size == 1) {
                        val bn = batches.first()["batch_no"] as? String ?: ""
                        if (bn.isNotEmpty()) selectBatch(bn) else _state.update { it.copy(isLoading = false) }
                    } else if (batches.size > 1) {
                        _activeBatches = batches
                        activeBatches.value = batches
                        _state.update { it.copy(isLoading = false, scanMsg = "有${batches.size}个未完成批次，请选择") }
                    }
                    _state.update { it.copy(isLoading = false) }
                },
                onFailure = { _state.update { it.copy(isLoading = false) } }
            )
        }
    }

    val activeBatches = MutableStateFlow<List<Map<String, Any?>>>(emptyList())
    private var _activeBatches: List<Map<String, Any?>> = emptyList()

    fun loadBatches() {
        viewModelScope.launch {
            repo.getActiveRefillBatches().fold(
                onSuccess = { _activeBatches = it.data ?: emptyList(); activeBatches.value = _activeBatches },
                onFailure = {}
            )
        }
    }

    fun selectBatch(batchNo: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.getRefillBatchDetail(batchNo).fold(
                onSuccess = { loadBatch(it.data) },
                onFailure = { _state.update { it.copy(isLoading = false, scanMsg = "加载失败") } }
            )
        }
    }

    private fun loadBatch(data: Map<String, Any?>?) {
        if (data == null) { _state.update { it.copy(isLoading = false) }; return }
        val step = (data["step"] as? Double)?.toInt() ?: 0
        if (step <= 0) { _state.update { it.copy(isLoading = false) }; return }
        val partsRaw = data["parts"] as? List<*> ?: emptyList<Any>()
        val parts = partsRaw.mapNotNull { p ->
            val m = p as? Map<*, *> ?: return@mapNotNull null
            PendingItem(prepDetailId = (m["prep_detail_id"] as? Double)?.toInt() ?: 0,
                prepOrderId = (m["prep_order_id"] as? Double)?.toInt() ?: 0,
                prepOrderNo = m["prep_order_no"] as? String ?: "", productName = m["product_name"] as? String ?: "",
                partId = (m["part_id"] as? Double)?.toInt() ?: 0, partNo = m["part_no"] as? String ?: "",
                requiredQty = 0.0, actualQty = 0.0, remaining = 0.0,
                locationCodes = (m["location_codes"] as? List<*>)?.mapNotNull { it as? String } ?: emptyList())
        }
        val pickedIds = (data["picked_ids"] as? List<*>)?.mapNotNull { (it as? Double)?.toInt() }?.toSet() ?: emptySet()
        val selectedIds = (data["selected_ids"] as? List<*>)?.mapNotNull { (it as? Double)?.toInt() }?.toSet() ?: emptySet()
        val batchNo = (data["batch_no"] as? String)?.ifEmpty { null } ?: "RF${System.currentTimeMillis()}"
        _state.update { it.copy(isLoading = false, parts = parts, selectedIds = selectedIds,
            pickedIds = pickedIds, step = step, batchNo = batchNo,
            scanMsg = "恢复: ${data["product_name"]} (${parts.size}项)") }
    }

    fun scanProduct(barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null, boxPartNo = "", boxPart = null) }
            repo.getRefillParts(barcode.trim()).fold(
                onSuccess = { res ->
                    val items = res.data ?: emptyList()
                    if (items.isEmpty())
                        _state.update { it.copy(isLoading = false, scanMsg = "未找到产品: ${barcode.trim()}") }
                    else
                        _state.update { it.copy(isLoading = false, parts = items, selectedIds = emptySet(),
                            pickedIds = emptySet(), verifiedIds = emptySet(), step = 1) }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 料号匹配：扫描条码(>14位)包含清单料号即匹配 */
    private fun matchPart(barcode: String): PendingItem? {
        val trimmed = barcode.trim()
        return _state.value.parts.firstOrNull {
            it.partNo.trim().let { pn -> trimmed.equals(pn, ignoreCase = true) || trimmed.contains(pn, ignoreCase = true) }
        }
    }

    /** 步骤1：扫部品条码(>14位)勾选 */
    fun togglePart(barcode: String) {
        val trimmed = barcode.trim()
        if (trimmed.length <= 14) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "请扫部品条码(>14位)") }; return }
        val match = matchPart(trimmed)
        if (match == null) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "未匹配: $trimmed") }; return }
        ScanSoundManager.playSuccess()
        val ids = _state.value.selectedIds
        _state.update { it.copy(
            selectedIds = if (match.prepDetailId in ids) ids - match.prepDetailId else ids + match.prepDetailId,
            scanMsg = if (match.prepDetailId in ids) "取消: ${match.partNo}" else "已选: ${match.partNo}"
        )}
    }

    fun startRefill() {
        val selected = _state.value.parts.filter { it.prepDetailId in _state.value.selectedIds }
        if (selected.isEmpty()) return
        val batchNo = "RF${System.currentTimeMillis()}"
        val items = selected.map { RefillStartItem(it.prepDetailId, it.prepOrderId, it.partNo, it.productName, it.locationCodes.firstOrNull() ?: "") }
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, batchNo = batchNo) }
            repo.batchStartRefill(batchNo, items).fold(
                onSuccess = { ScanSoundManager.playSuccess(); _state.update { it.copy(isLoading = false, step = 2, pickedIds = emptySet(), scanMsg = "开始取料") } },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 步骤2：扫部品条码(>14位)取料，可多次扫同一料号 */
    fun scanPick(barcode: String) {
        val trimmed = barcode.trim()
        if (trimmed.length <= 14) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "请扫部品条码(>14位)") }; return }
        val match = matchPart(trimmed)
        if (match == null) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "料号不匹配: $trimmed") }; return }
        if (match.prepDetailId !in _state.value.selectedIds) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "该料号未勾选") }; return }
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.scanRefill(match.prepDetailId, match.prepOrderId, match.partNo, match.productName,
                match.locationCodes.firstOrNull() ?: "", trimmed, _state.value.batchNo, 2).fold(
                onSuccess = { ScanSoundManager.playSuccess(); _state.update { it.copy(isLoading = false, pickedIds = it.pickedIds + match.prepDetailId, scanMsg = "取料: ${match.partNo}") } },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    /** 步骤3：核对扫描。≤14位=料盒先识别，>14位=直接匹配部品确认 */
    fun scanVerify(barcode: String) {
        val trimmed = barcode.trim()
        val s = _state.value
        val sel = s.parts.filter { it.prepDetailId in s.selectedIds }

        if (trimmed.length <= 14) {
            // 料盒条码(≤14位)：记录当前料盒，等待扫部品
            val match = sel.firstOrNull { it.partNo.trim().contains(trimmed, ignoreCase = true) }
            if (match == null) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "料盒不匹配: $trimmed") }; return }
            ScanSoundManager.playSuccess()
            _state.update { it.copy(boxPartNo = trimmed, boxPart = match, scanMsg = "料盒→${match.partNo}，扫部品确认") }
            return
        }
        // 部品条码(>14位)：有料盒则验证包含关系，无则直接匹配
        val part: PendingItem?
        if (s.boxPartNo.isNotEmpty()) {
            if (!trimmed.contains(s.boxPartNo, ignoreCase = true)) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "与料盒${s.boxPartNo}不匹配") }; return }
            part = s.boxPart!!
        } else {
            part = sel.firstOrNull { it.partNo.trim().equals(trimmed, ignoreCase = true) || trimmed.contains(it.partNo.trim(), ignoreCase = true) }
            if (part == null) { ScanSoundManager.playError(); _state.update { it.copy(scanMsg = "料号不匹配: $trimmed") }; return }
        }
        doVerify(part, trimmed)
    }

    private fun doVerify(part: PendingItem, barcode: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true) }
            repo.scanRefill(part.prepDetailId, part.prepOrderId, part.partNo, part.productName,
                part.locationCodes.firstOrNull() ?: "", barcode, _state.value.batchNo, 3).fold(
                onSuccess = { ScanSoundManager.playSuccess(); _state.update { it.copy(isLoading = false, verifiedIds = it.verifiedIds + part.prepDetailId, boxPartNo = "", boxPart = null, scanMsg = "已核对: ${part.partNo}") } },
                onFailure = { e -> ScanSoundManager.playError(); _state.update { it.copy(isLoading = false, scanMsg = e.message) } }
            )
        }
    }

    fun goPickDone() { _state.update { it.copy(step = 3, verifiedIds = emptySet(), boxPartNo = "", boxPart = null, scanMsg = "请先扫料盒(≤14位)") } }
    fun goDone() {
        _state.value = RefillUiState()
    }

    fun clearAll() { _state.value = RefillUiState() }
    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
