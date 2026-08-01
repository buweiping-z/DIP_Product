package com.dip.material.data.repository

import android.content.Context
import com.dip.material.data.models.*
import com.dip.material.data.network.RetrofitClient

class AppRepository(val context: Context) {
    private val api get() = RetrofitClient.getApiService(context)

    private suspend fun <T> call(block: suspend () -> T): Result<T> {
        return try { Result.success(block()) }
        catch (e: Exception) { Result.failure(Exception("网络连接失败: ${e.message}")) }
    }

    // Auth
    suspend fun login(username: String, password: String) = call { api.login(LoginRequest(username, password)) }
    suspend fun getCurrentUser() = call { api.getCurrentUser() }

    // Dashboard
    suspend fun getDashboardStats() = call { api.getDashboardStats() }

    // Parts & Locations
    suspend fun searchParts(partNo: String) = call { api.getParts(partNo = partNo, pageSize = 5) }
    suspend fun searchLocations(locationCode: String) = call { api.getLocations(locationCode = locationCode, pageSize = 500) }
    suspend fun getAvailableInventory(partId: Int) = call { api.getAvailableInventory(partId) }
    suspend fun checkLocation(locationCode: String, partId: Int) = call { api.checkLocation(locationCode, partId) }

    // Shelving
    suspend fun directShelving(barcode: String, targetLocationCode: String, quantity: Double) =
        call { api.directShelving(DirectShelvingRequest(barcode, targetLocationCode, quantity)) }

    // Prep
    suspend fun getPrepOrders(status: Int? = null) = call { api.getPrepOrders(status = status) }
    suspend fun getPrepDetail(prepId: Int) = call { api.getPrepDetail(prepId) }
    suspend fun scanPrepItem(prepId: Int, barcode: String, detailId: Int? = null): Result<Map<String, Any?>> {
        return try { Result.success(api.scanPrepItem(prepId, PrepScanRequest(barcode, detailId))) }
        catch (e: Exception) { Result.failure(Exception("网络连接失败: ${e.message}")) }
    }
    suspend fun checkKitComplete(prepId: Int) = call { api.checkKitComplete(prepId) }
    suspend fun finishPrep(prepId: Int, detailIds: List<Int>) = call { api.finishPrep(prepId, FinishPrepRequest(detailIds)) }

    // Dashboard
    suspend fun getMobileCounts() = call { api.getMobileCounts() }

    // Refill
    suspend fun getActiveRefillBatches() = call { api.getActiveRefillBatches() }
    suspend fun getRefillBatchDetail(batchNo: String) = call { api.getRefillBatchDetail(batchNo) }
    suspend fun getRefillParts(productName: String) = call { api.getRefillParts(productName = productName) }
    suspend fun getRefillPartsByOrder(orderNo: String) = call { api.getRefillParts(orderNo = orderNo) }
    suspend fun getRefillRecords(partNo: String? = null, locationCode: String? = null,
                                  startDate: String? = null, endDate: String? = null) =
        call { api.getRefillRecords(partNo, locationCode, startDate, endDate) }
    suspend fun batchStartRefill(batchNo: String, items: List<RefillStartItem>) =
        call { api.batchStartRefill(RefillBatchStartRequest(batchNo, items)) }
    suspend fun scanRefill(detailId: Int, prepOrderId: Int, partNo: String, partName: String,
                            locationCode: String, barcode: String, batchNo: String, step: Int) =
        call { api.scanRefill(RefillScanRequest(detailId, prepOrderId, partNo, partName, locationCode, barcode, batchNo, step)) }

    // Return
    suspend fun scanReturn(barcode: String, locationId: Int) =
        call { api.scanReturn(ReturnScanRequest(barcode, locationId)) }
    suspend fun batchFinishReturn(targetLocationId: Int, items: List<Map<String, Any?>>) =
        call { api.batchFinishReturn(mapOf("target_location_id" to targetLocationId, "items" to items)) }
    suspend fun getReturnList() = call { api.getReturnList() }

    // Orders
    suspend fun getOrders(status: Int) = call { api.getOrders(status) }
    suspend fun getOrderDetail(orderId: Int) = call { api.getOrderDetail(orderId) }

    // Outbound
    suspend fun getOutboundOrders(status: Int? = null) = call { api.getOutboundOrders(status = status) }
    suspend fun getOutboundOrderDetail(orderId: Int) = call { api.getOutboundOrderDetail(orderId) }
    suspend fun confirmOutboundDetail(orderId: Int, detailId: Int, barcode: String) =
        call { api.confirmOutboundDetail(orderId, detailId, OutboundConfirmRequest(barcode)) }
    suspend fun confirmOutboundAll(orderId: Int) = call { api.confirmOutboundAll(orderId) }

    // Online
    suspend fun confirmOnline(detailId: Long, barcode: String, quantity: Double = 1.0) =
        call { api.confirmOnline(OnlineConfirmRequest(detailId, barcode, quantity)) }

    // Substitute Orders
    suspend fun getSubstituteOrders(status: Int = 1): Result<ApiResponse<PageResult<SubstituteOrderItem>>> =
        call { api.getSubstituteOrders(status = status) }

    suspend fun getSubstituteOrderDetails(orderId: Int): Result<ApiResponse<SubstituteOrderDetail>> =
        call { api.getSubstituteOrderDetails(orderId) }

    suspend fun confirmSubstituteDetail(orderId: Int, detailId: Int): Result<ApiResponse<Map<String, Any?>>> =
        call { api.confirmSubstituteDetail(orderId, detailId) }

    suspend fun confirmSubstituteAll(orderId: Int): Result<ApiResponse<Map<String, Any?>>> =
        call { api.confirmSubstituteAll(orderId) }

    // Changeover
    suspend fun getChangeoverBom(productName: String): Result<List<Map<String, Any?>>> {
        return try {
            val res = api.getChangeoverBom(productName = productName)
            if (res.code == 0 && res.data != null) Result.success(res.data)
            else Result.success(emptyList())
        } catch (e: Exception) {
            Result.failure(Exception("网络连接失败: ${e.message}"))
        }
    }

    suspend fun getChangeoverBomByOrder(orderNo: String): Result<Pair<String, List<Map<String, Any?>>>> {
        return try {
            val res = api.getChangeoverBomByOrder(orderNo)
            if (res.code == 0 && res.data != null) {
                val data = res.data
                val productName = data["product_name"] as? String ?: orderNo
                val bom = (data["bom"] as? List<*>)?.filterIsInstance<Map<String, Any?>>() ?: emptyList()
                Result.success(Pair(productName, bom))
            } else {
                Result.success(Pair(orderNo, emptyList()))
            }
        } catch (e: Exception) {
            Result.failure(Exception("网络连接失败: ${e.message}"))
        }
    }

    suspend fun getChangeoverBatches(): Result<List<Map<String, Any?>>> {
        return call { api.getChangeoverBatches() }.map { it.data ?: emptyList() }
    }

    suspend fun createChangeoverBatch(productName: String, bom: List<Map<String, Any?>>): Result<Map<String, Any?>> {
        return call {
            api.createChangeoverBatch(mapOf("product_name" to productName, "bom" to bom))
        }.map { it.data ?: emptyMap() }
    }

    suspend fun getChangeoverBatchDetail(batchNo: String): Result<Map<String, Any?>> {
        return call { api.getChangeoverBatchDetail(batchNo) }.map { it.data ?: emptyMap() }
    }

    suspend fun scanChangeoverBatch(batchNo: String, partNo: String): Result<Map<String, Any?>> {
        return call { api.scanChangeoverBatch(batchNo, mapOf("part_no" to partNo)) }.map { it.data ?: emptyMap() }
    }

    suspend fun completeChangeoverBatch(batchNo: String): Result<Unit> {
        return call { api.completeChangeoverBatch(batchNo) }.map { }
    }

    // Call Material 叫料
    suspend fun callMaterial(items: List<CallMaterialItem>) = call { api.callMaterial(CallMaterialRequest(items)) }
}
