package com.dip.material.data.repository

import android.content.Context
import com.dip.material.data.models.*
import com.dip.material.data.network.RetrofitClient

class AppRepository(val context: Context) {
    private val api get() = RetrofitClient.getApiService(context)

    // Auth
    suspend fun login(username: String, password: String) = api.login(LoginRequest(username, password))
    suspend fun getCurrentUser() = api.getCurrentUser()

    // Dashboard
    suspend fun getDashboardStats() = api.getDashboardStats()

    // Parts & Locations
    suspend fun getParts() = api.getParts()
    suspend fun getLocations() = api.getLocations()
    suspend fun searchParts(partNo: String) = api.getParts(partNo = partNo)
    suspend fun searchLocations(locationCode: String) = api.getLocations(locationCode = locationCode)
    suspend fun getAvailableInventory(partId: Int) = api.getAvailableInventory(partId)

    // Prep
    suspend fun getPrepOrders(status: Int? = null) = api.getPrepOrders(status = status)
    suspend fun getPrepDetail(prepId: Int) = api.getPrepDetail(prepId)
    suspend fun scanPrepItem(prepId: Int, barcode: String, detailId: Int? = null) =
        api.scanPrepItem(prepId, PrepScanRequest(barcode, detailId))
    suspend fun checkKitComplete(prepId: Int) = api.checkKitComplete(prepId)

    // Refill
    suspend fun getPendingItems() = api.getPendingItems()
    suspend fun getRefillRecords() = api.getRefillRecords()

    // Return
    suspend fun scanReturn(barcode: String, locationId: Int) =
        api.scanReturn(ReturnScanRequest(barcode, locationId))
    suspend fun getReturnList() = api.getReturnList()

    // Shelving
    suspend fun directShelving(barcode: String, targetLocationCode: String, quantity: Double) =
        api.directShelving(DirectShelvingRequest(barcode, targetLocationCode, quantity))

    suspend fun getShelvingBatches(status: Int? = null) = api.getShelvingBatches(status = status)
    suspend fun getShelvingDetail(batchId: Int) = api.getShelvingDetail(batchId)
    suspend fun confirmShelving(batchId: Int) = api.confirmShelving(batchId)
    suspend fun scanShelvingItem(batchId: Int, barcode: String) =
        api.scanShelvingItem(batchId, ShelvingScanRequest(barcode, batchId))

    // Substitute
    suspend fun getSubstituteRecords() = api.getSubstituteRecords()
    suspend fun createSubstitute(originalPartId: Int, substitutePartId: Int,
                                 sourceLocationId: Int, targetLocationId: Int, quantity: Double) =
        api.createSubstitute(SubstituteRequest(originalPartId, substitutePartId, sourceLocationId, targetLocationId, quantity))
    suspend fun confirmSubstitute(id: Int) = api.confirmSubstitute(id)

    // Online
    suspend fun confirmOnline(prepOrderId: Int, partNo: String, barcode: String) =
        api.confirmOnline(OnlineConfirmRequest(prepOrderId, partNo, barcode))
    suspend fun getOnlineRecords() = api.getOnlineRecords()
}
