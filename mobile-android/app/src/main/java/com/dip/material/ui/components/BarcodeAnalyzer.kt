package com.dip.material.ui.components

import android.os.Handler
import android.os.Looper
import androidx.annotation.OptIn
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import kotlinx.coroutines.*
import java.util.concurrent.atomic.AtomicBoolean

/**
 * 扫码模式（移植自 machine_check）
 * - AUTO: ML Kit 原图识别（纸质/印刷码首选）
 * - PCB:  额外跑「定位→截取→前处理→再识别」增强通道
 */
enum class ScanMode { AUTO, PCB }

/**
 * 实时扫描统计快照（供 UI 诊断 HUD）
 */
data class PcbScanStats(
    val frames: Int = 0,
    val hits: Int = 0,
    val hitRate: Float = 0f,
    val avgDecodeMs: Long = 0L,
    val lastChannel: String = "-"
)

/**
 * 前处理驱动的条码分析器（移植自 machine_check，已精简）。
 * 双通道：
 * - 通道 A：ML Kit 原图（带旋转），纸质/印刷码
 * - 通道 B（PCB 模式）：ROI截取→对比拉伸→正反极性→ML Kit，专攻刻印码
 */
@OptIn(ExperimentalGetImage::class)
class BarcodeAnalyzer(
    private val onBarcodeScanned: (String) -> Unit,
    scanMode: ScanMode = ScanMode.AUTO,
    private val params: PcbTuneParams = PcbTuneParams(),
    private val onStatsUpdate: ((PcbScanStats) -> Unit)? = null
) : ImageAnalysis.Analyzer {

    var scanMode: ScanMode = scanMode
        set(value) { field = value }

    val isActive = AtomicBoolean(false)

    @Volatile private var lastScannedCode: String? = null
    @Volatile private var lastScanTime: Long = 0L
    private val dedupCooldownMs = 500L

    @Volatile private var grayBuffer: IntArray? = null
    @Volatile private var cropBuffer: IntArray? = null
    @Volatile private var lastCropW = 0
    @Volatile private var lastCropH = 0

    @Volatile private var statsFrames = 0
    @Volatile private var statsHits = 0
    @Volatile private var statsDecodeMs = 0L
    @Volatile private var statsLastChannel = "-"
    @Volatile private var lastDecodeMs = 0L
    @Volatile private var winChannel = "-"

    private val mlKitScanner = BarcodeScanning.getClient(
        BarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_ALL_FORMATS)
            .build()
    )

    private val analysisScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun analyze(imageProxy: ImageProxy) {
        if (!isActive.get()) { imageProxy.close(); return }

        val now = System.currentTimeMillis()
        if (now - lastScanTime < dedupCooldownMs) { imageProxy.close(); return }

        val mediaImage = imageProxy.image ?: run { imageProxy.close(); return }
        val w = imageProxy.width
        val h = imageProxy.height
        val gray = ImageUtils.fillGrayscaleFromYuv(mediaImage, grayBuffer, w, h)
            ?: run { imageProxy.close(); return }
        grayBuffer = gray

        // 取景框 ROI 裁剪
        val fw = params.roiWidthFraction
        val fh = params.roiHeightFraction
        val roiX = ((w * (1f - fw)) / 2f).toInt().coerceAtLeast(0)
        val roiY = ((h * (1f - fh)) / 2f).toInt().coerceAtLeast(0)
        val roiW = (w * fw).toInt().coerceIn(1, w - roiX)
        val roiH = (h * fh).toInt().coerceIn(1, h - roiY)

        val cropped = if (cropBuffer != null && cropBuffer!!.size >= roiW * roiH)
            cropBuffer!! else IntArray(roiW * roiH)
        for (y in 0 until roiH) {
            System.arraycopy(gray, (roiY + y) * w + roiX, cropped, y * roiW, roiW)
        }
        cropBuffer = cropped
        lastCropW = roiW
        lastCropH = roiH

        val isInFrame = { left: Int, top: Int, right: Int, bottom: Int ->
            left >= 0 && top >= 0 && right <= w && bottom <= h
        }

        val decided = AtomicBoolean(false)
        val t0 = System.currentTimeMillis()

        // 通道 A：ML Kit 原图（带旋转）
        val jobA = analysisScope.launch {
            try {
                val img = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
                suspendCancellableCoroutine<Unit> { cont ->
                    mlKitScanner.process(img)
                        .addOnSuccessListener { barcodes ->
                            for (barcode in barcodes) {
                                val bbox = barcode.boundingBox ?: continue
                                if (isInFrame(bbox.left, bbox.top, bbox.right, bbox.bottom)) {
                                    barcode.rawValue?.takeIf { it.isNotEmpty() }?.let {
                                        winChannel = "MLKit"
                                        reportResult(it)
                                        decided.set(true)
                                    }
                                }
                            }
                        }
                        .addOnCompleteListener { if (cont.isActive) cont.resumeWith(Result.success(Unit)) }
                }
            } catch (_: Exception) { }
        }

        // 通道 B（PCB 模式）：ROI→对比拉伸→正反极性→ML Kit
        val jobB = if (scanMode == ScanMode.PCB) {
            analysisScope.launch {
                if (decided.get()) return@launch
                val candidates = buildDecodeCandidates(cropped, roiW, roiH, params)
                for (bmp in candidates) {
                    if (decided.get()) break
                    try {
                        val img = InputImage.fromBitmap(bmp, 0)
                        suspendCancellableCoroutine<Unit> { cont ->
                            mlKitScanner.process(img)
                                .addOnSuccessListener { barcodes ->
                                    for (barcode in barcodes) {
                                        barcode.rawValue?.takeIf { it.isNotEmpty() }?.let {
                                            winChannel = "MLKit"
                                            reportResult(it)
                                            decided.set(true)
                                        }
                                    }
                                }
                                .addOnCompleteListener { if (cont.isActive) cont.resumeWith(Result.success(Unit)) }
                        }
                    } catch (_: Exception) { }
                }
            }
        } else null

        analysisScope.launch {
            try {
                withTimeout(2500L) {
                    jobA.join()
                    jobB?.join()
                }
            } catch (_: TimeoutCancellationException) {
                jobA.cancel()
                jobB?.cancel()
            } finally {
                lastDecodeMs = System.currentTimeMillis() - t0
                recordStats(decided.get())
                imageProxy.close()
            }
        }
    }

    /** 构造前处理候选图：对比拉伸 → 正/反极性 */
    private fun buildDecodeCandidates(
        roi: IntArray, w: Int, h: Int, p: PcbTuneParams
    ): List<android.graphics.Bitmap> {
        val enhanced = ImageUtils.contrastStretch(roi, w, h, p.stretchClipPct, p.stretchGamma)
        val inv = ImageUtils.invertedGrayscale(enhanced, w, h)
        val list = ArrayList<android.graphics.Bitmap>(2)
        list.add(ImageUtils.grayscaleToBitmap(enhanced, w, h))
        list.add(ImageUtils.grayscaleToBitmap(inv, w, h))
        return list
    }

    private fun recordStats(hit: Boolean) {
        statsFrames++
        if (hit) { statsHits++; statsDecodeMs += lastDecodeMs }
        statsLastChannel = if (hit) winChannel else "none"
        winChannel = "-"
        val rate = if (statsFrames > 0) statsHits * 100f / statsFrames else 0f
        val avg = if (statsHits > 0) statsDecodeMs / statsHits else 0L
        onStatsUpdate?.invoke(PcbScanStats(statsFrames, statsHits, rate, avg, statsLastChannel))
    }

    private fun reportResult(rawValue: String) {
        if (!isActive.get()) return
        synchronized(this) {
            val now = System.currentTimeMillis()
            if (rawValue == lastScannedCode && (now - lastScanTime) < dedupCooldownMs) return
            lastScannedCode = rawValue; lastScanTime = now
        }
        Handler(Looper.getMainLooper()).post { onBarcodeScanned(rawValue) }
    }

    fun close() {
        isActive.set(false)
        analysisScope.cancel()
        mlKitScanner.close()
    }
}
