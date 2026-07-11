package com.dip.material.ui.components

import androidx.annotation.OptIn
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.atomic.AtomicBoolean

@OptIn(ExperimentalGetImage::class)
class BarcodeAnalyzer(
    private val onBarcodeScanned: (String) -> Unit
) : ImageAnalysis.Analyzer {

    val isActive = AtomicBoolean(true)

    @Volatile private var lastCode: String? = null
    @Volatile private var lastTime: Long = 0L

    private val scanner = BarcodeScanning.getClient(
        BarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_ALL_FORMATS)
            .build()
    )

    override fun analyze(imageProxy: ImageProxy) {
        if (!isActive.get()) { imageProxy.close(); return }
        val now = System.currentTimeMillis()
        if (now - lastTime < 300) { imageProxy.close(); return } // 全局最小间隔

        val image = imageProxy.image ?: run { imageProxy.close(); return }
        val inputImage = InputImage.fromMediaImage(image, imageProxy.imageInfo.rotationDegrees)

        scanner.process(inputImage)
            .addOnSuccessListener { barcodes ->
                for (barcode in barcodes) {
                    barcode.rawValue?.takeIf { it.isNotEmpty() }?.let {
                        synchronized(this) {
                            // 同一条码不重复触发，必须换码才处理
                            if (it == lastCode) return@let
                            lastCode = it; lastTime = now
                        }
                        android.os.Handler(android.os.Looper.getMainLooper()).post { onBarcodeScanned(it) }
                    }
                }
            }
            .addOnCompleteListener { imageProxy.close() }
    }

    fun close() {
        isActive.set(false)
        scanner.close()
    }
}
