package com.dip.material.ui.changeover

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

data class ChangeoverBomItem(
    val partNo: String,
    val partName: String = "",
    val requiredQty: Double = 0.0,
    val scannedCount: Int = 0
)

data class ChangeoverUiState(
    val step: Int = 0,  // 0=批次列表, 1=扫描产品, 2=扫描部品确认
    val batches: List<Map<String, Any?>> = emptyList(),
    val batchNo: String = "",
    val productName: String = "",
    val bomItems: List<ChangeoverBomItem> = emptyList(),
    val scannedCounts: MutableMap<String, Int> = mutableMapOf(),
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val msgOk: Boolean = true,
    val scanEventId: Int = 0,
    val lastScanOk: Boolean = false,
    val allDone: Boolean = false
)

class ChangeoverViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(ChangeoverUiState())
    val state: StateFlow<ChangeoverUiState> = _state.asStateFlow()

    init { loadBatches() }

    /** 解析料号：≤14位取全部，>14位去掉末尾4位 */
    private fun parsePartNo(barcode: String): String {
        val t = barcode.trim()
        return if (t.length <= 14) t else t.substring(0, t.length - 4)
    }

    fun loadBatches() {
        viewModelScope.launch {
            repo.getChangeoverBatches().fold(
                onSuccess = {
                    val batches = it
                    if (batches.size == 1) {
                        val bn = batches.first()["batch_no"] as? String ?: ""
                        selectBatch(bn)
                    } else {
                        _state.update { s -> s.copy(batches = batches, step = 0) }
                    }
                },
                onFailure = { _state.update { it.copy(batches = emptyList(), step = 0) } }
            )
        }
    }

    /** 选择/恢复批次 */
    fun selectBatch(batchNo: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, batchNo = batchNo) }
            repo.getChangeoverBatchDetail(batchNo).fold(
                onSuccess = { data ->
                    val productName = data["product_name"] as? String ?: ""
                    val bomRaw = data["bom"] as? List<*> ?: emptyList<Any>()
                    val items = bomRaw.mapNotNull { b ->
                        val m = b as? Map<*, *> ?: return@mapNotNull null
                        ChangeoverBomItem(
                            partNo = m["part_no"] as? String ?: "",
                            partName = m["part_name"] as? String ?: "",
                            requiredQty = (m["required_qty"] as? Double) ?: 0.0,
                            scannedCount = ((m["scanned_count"] as? Double) ?: 0.0).toInt()
                        )
                    }
                    val counts = mutableMapOf<String, Int>()
                    items.forEach { if (it.scannedCount > 0) counts[it.partNo] = it.scannedCount }
                    val allDone = items.isNotEmpty() && items.all { (counts[it.partNo] ?: 0) > 0 }
                    _state.update { it.copy(isLoading = false, productName = productName, bomItems = items,
                        scannedCounts = counts, allDone = allDone, step = 2, scanMsg = "恢复批次: $productName", msgOk = true) }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, scanMsg = e.message, msgOk = false) } }
            )
        }
    }

    /** 步骤1：扫订单号 → 创建批次 */
    fun scanOrder(barcode: String) {
        val orderNo = barcode.trim()
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }
            repo.getChangeoverBomByOrder(orderNo).fold(
                onSuccess = { (productName, bomMaps) ->
                    if (bomMaps.isEmpty()) {
                        ScanSoundManager.playError()
                        _state.update { it.copy(isLoading = false, scanMsg = "未找到 BOM: $orderNo", msgOk = false,
                            scanEventId = it.scanEventId + 1, lastScanOk = false) }
                        return@fold
                    }
                    repo.createChangeoverBatch(productName, bomMaps).fold(
                        onSuccess = { batch ->
                            val batchNo = batch["batch_no"] as? String ?: ""
                            val items = bomMaps.mapNotNull { m ->
                                val pn = m["part_no"] as? String ?: return@mapNotNull null
                                ChangeoverBomItem(pn, m["part_name"] as? String ?: "", (m["required_qty"] as? Double) ?: 0.0)
                            }
                            ScanSoundManager.playSuccess()
                            _state.update { it.copy(isLoading = false, batchNo = batchNo, productName = productName,
                                bomItems = items, scannedCounts = mutableMapOf(), allDone = false, step = 2,
                                scanMsg = "${items.size} 种料号", msgOk = true, scanEventId = it.scanEventId + 1, lastScanOk = true) }
                        },
                        onFailure = { e ->
                            ScanSoundManager.playError()
                            _state.update { it.copy(isLoading = false, scanMsg = e.message, msgOk = false,
                                scanEventId = it.scanEventId + 1, lastScanOk = false) }
                        }
                    )
                },
                onFailure = { e ->
                    ScanSoundManager.playError()
                    _state.update { it.copy(isLoading = false, scanMsg = e.message, msgOk = false,
                        scanEventId = it.scanEventId + 1, lastScanOk = false) }
                }
            )
        }
    }

    /** 步骤2：扫部品条码 → 匹配 + 本地计数 + 后端同步 */
    fun scanChangeover(barcode: String) {
        val trimmed = barcode.trim()

        val partNo = parsePartNo(trimmed)
        val s = _state.value

        val match = s.bomItems.firstOrNull { it.partNo.trim().equals(partNo, ignoreCase = true) }
        if (match == null) {
            ScanSoundManager.playError()
            _state.update { it.copy(scanMsg = "未匹配到料号: $partNo", msgOk = false,
                scanEventId = it.scanEventId + 1, lastScanOk = false) }
            return
        }

        ScanSoundManager.playSuccess()
        val newCounts = s.scannedCounts.toMutableMap()
        newCounts[match.partNo] = (newCounts[match.partNo] ?: 0) + 1

        val allDone = s.bomItems.isNotEmpty() && s.bomItems.all { (newCounts[it.partNo] ?: 0) > 0 }

        _state.update { it.copy(scannedCounts = newCounts, scanMsg = "已确认: $partNo", msgOk = true,
            allDone = allDone, scanEventId = it.scanEventId + 1, lastScanOk = true) }

        // 异步同步到后端（fire-and-forget）
        val bn = s.batchNo
        viewModelScope.launch {
            repo.scanChangeoverBatch(bn, match.partNo)
        }
    }

    /** 标记批次完成 */
    fun markComplete() {
        val bn = _state.value.batchNo
        viewModelScope.launch { repo.completeChangeoverBatch(bn) }
    }

    /** 回到批次列表（不清数据，不自动选中，允许退出） */
    fun backToBatches() {
        _state.update { it.copy(step = 0, scanMsg = null) }
        loadBatchesOnlyShow()  // 只刷新列表显示，不自动选中
    }

    private fun loadBatchesOnlyShow() {
        viewModelScope.launch {
            repo.getChangeoverBatches().fold(
                onSuccess = { batches -> _state.update { s -> s.copy(batches = batches, step = 0) } },
                onFailure = { _state.update { s -> s.copy(batches = emptyList(), step = 0) } }
            )
        }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
