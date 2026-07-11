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

    // Refill
    suspend fun getPendingItems() = call { api.getPendingItems() }
    suspend fun getRefillRecords() = call { api.getRefillRecords() }

    // Return
    suspend fun scanReturn(barcode: String, locationId: Int) =
        call { api.scanReturn(ReturnScanRequest(barcode, locationId)) }
    suspend fun getReturnList() = call { api.getReturnList() }

    // Orders
    suspend fun getOrders(status: Int) = call { api.getOrders(status) }
    suspend fun getOrderDetail(orderId: Int) = call { api.getOrderDetail(orderId) }

    // Online
    suspend fun confirmOnline(detailId: Long, barcode: String, quantity: Double = 1.0) =
        call { api.confirmOnline(OnlineConfirmRequest(detailId, barcode, quantity)) }

    // Substitute
    suspend fun createSubstitute(originalPartId: Int, substitutePartId: Int,
                                 sourceLocationId: Int, targetLocationId: Int, quantity: Double) =
        call { api.createSubstitute(SubstituteRequest(originalPartId, substitutePartId, sourceLocationId, targetLocationId, quantity)) }
}
