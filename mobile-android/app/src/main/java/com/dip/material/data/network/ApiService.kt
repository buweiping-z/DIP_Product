package com.dip.material.data.network

import com.dip.material.data.models.*
import retrofit2.http.*

interface ApiService {
    // ===== Auth =====
    @POST("api/v1/auth/login")
    suspend fun login(@Body request: LoginRequest): ApiResponse<LoginResponse>

    @POST("api/v1/auth/refresh")
    suspend fun refreshToken(@Body request: RefreshRequest): ApiResponse<LoginResponse>

    @GET("api/v1/auth/me")
    suspend fun getCurrentUser(): ApiResponse<UserInfo>

    // ===== Dashboard =====
    @GET("api/v1/dashboard/stats")
    suspend fun getDashboardStats(): ApiResponse<DashboardStats>

    // ===== Parts & Locations =====
    @GET("api/v1/parts")
    suspend fun getParts(
        @Query("part_no") partNo: String? = null, @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 200
    ): ApiResponse<PageResult<PartItem>>

    @GET("api/v1/locations")
    suspend fun getLocations(
        @Query("location_code") locationCode: String? = null, @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 200
    ): ApiResponse<PageResult<LocationItem>>

    @GET("api/v1/inventory/available/{partId}")
    suspend fun getAvailableInventory(@Path("partId") partId: Int): ApiResponse<List<InventoryAvailable>>

    // ===== Shelving =====
    @POST("api/v1/shelving/direct")
    suspend fun directShelving(@Body request: DirectShelvingRequest): ApiResponse<ShelvingRecord>

    // ===== Prep =====
    @GET("api/v1/prep")
    suspend fun getPrepOrders(
        @Query("status") status: Int? = null, @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50
    ): ApiResponse<PageResult<PrepOrderItem>>

    @GET("api/v1/prep/{prepId}/details")
    suspend fun getPrepDetail(@Path("prepId") prepId: Int): ApiResponse<PrepDetail>

    @POST("api/v1/prep/{prepId}/scan")
    suspend fun scanPrepItem(@Path("prepId") prepId: Int, @Body request: PrepScanRequest): ApiResponse<PrepScanResult>

    @POST("api/v1/prep/{prepId}/kit-check")
    suspend fun checkKitComplete(@Path("prepId") prepId: Int): ApiResponse<PrepScanResult>

    // ===== Refill =====
    @GET("api/v1/prep/pending")
    suspend fun getPendingItems(): ApiResponse<List<PendingItem>>

    @GET("api/v1/prep/refills")
    suspend fun getRefillRecords(@Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50): ApiResponse<PageResult<Any>>

    // ===== Return =====
    @POST("api/v1/return/scan")
    suspend fun scanReturn(@Body request: ReturnScanRequest): ApiResponse<PrepScanResult>

    @GET("api/v1/return")
    suspend fun getReturnList(@Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50): ApiResponse<PageResult<ReturnOrderItem>>

    // ===== Online =====
    @POST("api/v1/online/confirm")
    suspend fun confirmOnline(@Body request: OnlineConfirmRequest): ApiResponse<PrepScanResult>

    // ===== Substitute =====
    @POST("api/v1/inventory/substitute")
    suspend fun createSubstitute(@Body request: SubstituteRequest): ApiResponse<PrepScanResult>
}
