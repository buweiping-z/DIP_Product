package com.dip.material.data.models

import com.google.gson.annotations.SerializedName

// ===== API Envelope =====
data class ApiResponse<T>(val code: Int, val data: T?, val message: String?) {
    val isSuccess get() = code == 0
}
data class PageResult<T>(
    val total: Int, val page: Int,
    @SerializedName("page_size") val pageSize: Int,
    val items: List<T>
)

// ===== Auth =====
data class LoginRequest(val username: String, val password: String)
data class LoginResponse(
    @SerializedName("access_token") val accessToken: String,
    @SerializedName("refresh_token") val refreshToken: String,
    @SerializedName("expires_in") val expiresIn: Int,
    val user: UserInfo
)
data class RefreshRequest(@SerializedName("refresh_token") val refreshToken: String)
data class UserInfo(
    val id: Int, val username: String,
    @SerializedName("real_name") val realName: String,
    @SerializedName("role_code") val roleCode: String?
)

// ===== Dashboard =====
data class DashboardStats(
    @SerializedName("order_stats") val orderStats: OrderStats?,
    @SerializedName("prep_stats") val prepStats: PrepStats?,
    @SerializedName("today_ops") val todayOps: TodayOps?
)
data class OrderStats(val total: Int, val pending: Int, @SerializedName("in_progress") val inProgress: Int, val done: Int, val cancelled: Int)
data class PrepStats(val total: Int, val pending: Int, val done: Int, val cancelled: Int)
data class TodayOps(@SerializedName("prep_scans") val prepScans: Int, val returns: Int, val shelving: Int)

// ===== Parts & Locations =====
data class PartItem(val id: Int, @SerializedName("part_no") val partNo: String, @SerializedName("part_name") val partName: String)
data class LocationItem(val id: Int, @SerializedName("location_code") val locationCode: String)
data class InventoryAvailable(
    val id: Int, @SerializedName("part_id") val partId: Int,
    @SerializedName("part_no") val partNo: String, @SerializedName("part_name") val partName: String,
    @SerializedName("location_id") val locationId: Int, @SerializedName("location_code") val locationCode: String,
    @SerializedName("total_qty") val totalQty: Double,
    @SerializedName("available_qty") val availableQty: Double,
    @SerializedName("frozen_qty") val frozenQty: Double = 0.0
)

// ===== Shelving =====
data class DirectShelvingRequest(
    val barcode: String,
    @SerializedName("target_location_code") val targetLocationCode: String,
    val quantity: Double
)
data class ShelvingRecord(
    val id: Int, @SerializedName("part_no") val partNo: String,
    @SerializedName("part_name") val partName: String,
    @SerializedName("target_location_code") val targetLocationCode: String,
    val quantity: Double, @SerializedName("operator_id") val operatorId: Int,
    @SerializedName("loaded_at") val loadedAt: String?
)

// ===== Prep =====
data class PrepOrderItem(
    val id: Int, @SerializedName("order_no") val orderNo: String,
    @SerializedName("production_order_id") val productionOrderId: Int,
    @SerializedName("line_id") val lineId: Int, val status: Int,
    @SerializedName("kit_check_result") val kitCheckResult: Int
)
data class PrepDetail(
    val id: Int, @SerializedName("order_no") val orderNo: String,
    @SerializedName("product_name") val productName: String,
    @SerializedName("line_id") val lineId: Int, val status: Int,
    val details: List<PrepDetailItem>?
)
data class PrepDetailItem(
    val id: Int, @SerializedName("part_id") val partId: Int,
    @SerializedName("part_no") val partNo: String,
    @SerializedName("required_qty") val requiredQty: Double,
    @SerializedName("total_required_qty") val totalRequiredQty: Double = 0.0,
    @SerializedName("actual_qty") val actualQty: Double, val status: Int,
    val stocks: List<PartStock>? = null
)

data class PartStock(
    @SerializedName("location_code") val locationCode: String,
    @SerializedName("location_id") val locationId: Int,
    @SerializedName("available_qty") val availableQty: Double
)
data class PrepScanRequest(val barcode: String, @SerializedName("prep_detail_id") val prepDetailId: Int? = null)

data class PrepScanResult(
    val matched: Boolean = false,
    @SerializedName("part_no") val partNo: String? = null,
    @SerializedName("actual_qty") val actualQty: Double = 0.0,
    @SerializedName("required_qty") val requiredQty: Double = 0.0,
    val status: Int = 0,
    @SerializedName("all_done") val allDone: Boolean = false,
    val message: String? = null
)
data class PendingItem(
    @SerializedName("prep_detail_id") val prepDetailId: Int,
    @SerializedName("prep_order_id") val prepOrderId: Int,
    @SerializedName("prep_order_no") val prepOrderNo: String,
    @SerializedName("product_name") val productName: String,
    @SerializedName("part_id") val partId: Int, @SerializedName("part_no") val partNo: String,
    @SerializedName("required_qty") val requiredQty: Double,
    @SerializedName("actual_qty") val actualQty: Double, val remaining: Double
)

// ===== Return =====
data class ReturnScanRequest(val barcode: String, @SerializedName("target_location_id") val targetLocationId: Int)
data class ReturnOrderItem(
    val id: Int, @SerializedName("order_no") val orderNo: String,
    @SerializedName("prep_order_id") val prepOrderId: Int?,
    @SerializedName("return_reason") val returnReason: String, val status: Int,
    @SerializedName("created_at") val createdAt: String?
)

// ===== Online =====
data class OnlineConfirmRequest(
    @SerializedName("prep_order_id") val prepOrderId: Int,
    @SerializedName("part_no") val partNo: String, val barcode: String,
    @SerializedName("req_qty") val reqQty: Double? = null,
    @SerializedName("station_id") val stationId: Int? = null
)

// ===== Substitute =====
data class SubstituteRequest(
    @SerializedName("original_part_id") val originalPartId: Int,
    @SerializedName("substitute_part_id") val substitutePartId: Int,
    @SerializedName("source_location_id") val sourceLocationId: Int,
    @SerializedName("target_location_id") val targetLocationId: Int,
    val quantity: Double
)
