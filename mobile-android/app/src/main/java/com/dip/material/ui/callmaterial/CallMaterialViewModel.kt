package com.dip.material.ui.callmaterial

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.models.CallMaterialItem
import com.dip.material.data.models.InventoryAvailable
import com.dip.material.data.repository.AppRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class CallMaterialUiState(
    val items: List<CallMaterialItem> = emptyList(),
    val isLoading: Boolean = false,
    val scanMsg: String? = null,
    val msgOk: Boolean = true,
    val uploading: Boolean = false
)

class CallMaterialViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val _state = MutableStateFlow(CallMaterialUiState())
    val state: StateFlow<CallMaterialUiState> = _state.asStateFlow()

    /** 扫描料号 → 解析 → 匹配部品 → 查库存 → 加入列表 */
    fun scanPart(barcode: String, onBack: () -> Unit) {
        val trimmed = barcode.trim()
        // 解析：>14位去末尾4位，≤14位取全部
        val parsed = if (trimmed.length > 14) trimmed.dropLast(4) else trimmed

        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, scanMsg = null) }

            // 1. 匹配部品
            val partRes = repo.searchParts(parsed)
            partRes.fold(
                onSuccess = { res ->
                    val partItems = res.data?.items ?: emptyList()
                    if (partItems.isEmpty()) {
                        _state.update { it.copy(isLoading = false, scanMsg = "料号未找到: $parsed", msgOk = false) }
                        return@fold
                    }
                    val part = partItems.first()
                    val partNo = part.partNo
                    val partId = part.id

                    // 2. 查库存
                    repo.getAvailableInventory(partId).fold(
                        onSuccess = { invRes ->
                            val invList: List<InventoryAvailable> = invRes.data ?: emptyList()
                            if (invList.isEmpty() || invList.all { it.availableQty <= 0 }) {
                                _state.update { it.copy(isLoading = false, scanMsg = "该料号无库存记录: $partNo", msgOk = false) }
                                return@fold
                            }
                            val locationCode = invList.filter { it.availableQty > 0 }
                                .joinToString(",") { it.locationCode }

                            // 3. 去重检查
                            val existing = _state.value.items.any { it.partNo == partNo }
                            if (existing) {
                                _state.update { it.copy(isLoading = false, scanMsg = "已添加: $partNo", msgOk = true) }
                                return@fold
                            }

                            val newItem = CallMaterialItem(partNo = partNo, partId = partId, locationCode = locationCode)
                            _state.update { it.copy(isLoading = false,
                                items = it.items + newItem,
                                scanMsg = "已添加: $partNo ($locationCode)",
                                msgOk = true) }
                        },
                        onFailure = { e ->
                            _state.update { it.copy(isLoading = false, scanMsg = e.message, msgOk = false) }
                        }
                    )
                },
                onFailure = { e ->
                    _state.update { it.copy(isLoading = false, scanMsg = e.message, msgOk = false) }
                }
            )
        }
    }

    /** 上传叫料数据 */
    fun upload(onBack: () -> Unit) {
        val items = _state.value.items
        if (items.isEmpty()) return

        viewModelScope.launch {
            _state.update { it.copy(uploading = true) }
            repo.callMaterial(items).fold(
                onSuccess = { res ->
                    val count = (res.data?.get("count") as? Double)?.toInt() ?: items.size
                    _state.update { it.copy(uploading = false, scanMsg = "叫料成功，共 $count 项", msgOk = true) }
                    onBack()
                },
                onFailure = { e ->
                    _state.update { it.copy(uploading = false, scanMsg = e.message, msgOk = false) }
                }
            )
        }
    }

    /** 删除已添加项 */
    fun removeItem(index: Int) {
        _state.update { it.copy(items = it.items.filterIndexed { i, _ -> i != index }) }
    }

    fun clearMsg() { _state.update { it.copy(scanMsg = null) } }
}
