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

    @GET("api/v1/inventory/check-location")
    suspend fun checkLocation(@Query("location_code") locationCode: String, @Query("part_id") partId: Int): ApiResponse<Map<String, Any?>>

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
    suspend fun scanPrepItem(@Path("prepId") prepId: Int, @Body request: PrepScanRequest): Map<String, Any?>

    @POST("api/v1/prep/{prepId}/finish")
    suspend fun finishPrep(@Path("prepId") prepId: Int, @Body request: FinishPrepRequest): ApiResponse<Any?>

    @POST("api/v1/prep/{prepId}/kit-check")
    suspend fun checkKitComplete(@Path("prepId") prepId: Int): ApiResponse<PrepScanResult>

    // ===== Refill =====
    @GET("api/v1/refill/active")
    suspend fun getActiveRefillBatches(): ApiResponse<List<Map<String, Any?>>>

    @GET("api/v1/refill/batch/{batchNo}")
    suspend fun getRefillBatchDetail(@Path("batchNo") batchNo: String): ApiResponse<Map<String, Any?>>

    @GET("api/v1/refill/parts")
    suspend fun getRefillParts(@Query("product_name") productName: String): ApiResponse<List<PendingItem>>

    @GET("api/v1/refill")
    suspend fun getRefillRecords(
        @Query("part_no") partNo: String? = null, @Query("location_code") locationCode: String? = null,
        @Query("start_date") startDate: String? = null, @Query("end_date") endDate: String? = null,
        @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50
    ): ApiResponse<PageResult<Any>>

    @POST("api/v1/refill/batch-start")
    suspend fun batchStartRefill(@Body request: RefillBatchStartRequest): ApiResponse<Map<String, Any?>>

    @POST("api/v1/refill/scan")
    suspend fun scanRefill(@Body request: RefillScanRequest): ApiResponse<Map<String, Any?>>

    // ===== Return =====
    @POST("api/v1/return/scan")
    suspend fun scanReturn(@Body request: ReturnScanRequest): ApiResponse<PrepScanResult>

    @POST("api/v1/return/batch-finish")
    suspend fun batchFinishReturn(@Body request: Map<String, @JvmSuppressWildcards Any?>): ApiResponse<Map<String, Any?>>

    @GET("api/v1/return")
    suspend fun getReturnList(@Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50): ApiResponse<PageResult<ReturnOrderItem>>

    // ===== Orders =====
    @GET("api/v1/orders")
    suspend fun getOrders(@Query("status") status: Int): ApiResponse<PageResult<OrderItem>>

    @GET("api/v1/orders/{id}/details")
    suspend fun getOrderDetail(@Path("id") orderId: Int): ApiResponse<OrderDetail>

    // ===== Outbound =====
    @GET("api/v1/outbound")
    suspend fun getOutboundOrders(
        @Query("status") status: Int? = null, @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50
    ): ApiResponse<PageResult<OutboundOrderItem>>

    @GET("api/v1/outbound/{id}")
    suspend fun getOutboundOrderDetail(@Path("id") orderId: Int): ApiResponse<OutboundOrderDetail>

    @POST("api/v1/outbound/{id}/details/{detailId}/confirm")
    suspend fun confirmOutboundDetail(
        @Path("id") orderId: Int, @Path("detailId") detailId: Int,
        @Body request: OutboundConfirmRequest
    ): ApiResponse<Map<String, Any?>>

    @POST("api/v1/outbound/{id}/confirm")
    suspend fun confirmOutboundAll(@Path("id") orderId: Int): ApiResponse<Map<String, Any?>>

    // ===== Online =====
    @POST("api/v1/online/confirm")
    suspend fun confirmOnline(@Body request: OnlineConfirmRequest): ApiResponse<Map<String, Any?>>

    // ===== Substitute Orders =====
    @GET("api/v1/substitute/orders")
    suspend fun getSubstituteOrders(
        @Query("status") status: Int? = 1, @Query("page") page: Int = 1, @Query("page_size") pageSize: Int = 50
    ): ApiResponse<PageResult<SubstituteOrderItem>>

    @GET("api/v1/substitute/orders/{id}/details")
    suspend fun getSubstituteOrderDetails(@Path("id") orderId: Int): ApiResponse<SubstituteOrderDetail>

    @POST("api/v1/substitute/orders/{id}/details/{detailId}/confirm")
    suspend fun confirmSubstituteDetail(@Path("id") orderId: Int, @Path("detailId") detailId: Int): ApiResponse<Map<String, Any?>>

    @POST("api/v1/substitute/orders/{id}/confirm")
    suspend fun confirmSubstituteAll(@Path("id") orderId: Int): ApiResponse<Map<String, Any?>>
}
