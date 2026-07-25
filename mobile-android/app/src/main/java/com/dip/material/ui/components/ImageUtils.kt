package com.dip.material.ui.components

import android.graphics.Bitmap
import android.media.Image
import java.nio.ByteBuffer

/**
 * 图像工具类（移植自 machine_check）。
 * 提供 YUV→灰度提取、对比拉伸、Bitmap 转换、极性反转等基础算子。
 */
object ImageUtils {

    fun fillGrayscaleFromYuv(image: Image, buffer: IntArray?, width: Int, height: Int): IntArray? {
        val yPlane = image.planes.getOrNull(0) ?: return null
        val yBuffer: ByteBuffer = yPlane.buffer
        val rowStride = yPlane.rowStride
        val pixelStride = yPlane.pixelStride
        val out = if (buffer != null && buffer.size >= width * height) buffer
                   else IntArray(width * height)

        if (pixelStride == 1) {
            for (y in 0 until height) {
                val offset = y * rowStride
                for (x in 0 until width) {
                    out[y * width + x] = yBuffer.get(offset + x).toInt() and 0xFF
                }
            }
        } else {
            for (y in 0 until height) {
                val rowOffset = y * rowStride
                for (x in 0 until width) {
                    out[y * width + x] = yBuffer.get(rowOffset + x * pixelStride).toInt() and 0xFF
                }
            }
        }
        return out
    }

    fun contrastStretch(
        pixels: IntArray, width: Int, height: Int,
        clipPct: Double = 0.0, gamma: Double = 1.0
    ): IntArray {
        val n = pixels.size
        if (n == 0) return pixels.copyOf()

        val hist = IntArray(256)
        for (p in pixels) hist[p.coerceIn(0, 255)]++

        var lo = 0
        while (lo < 255 && hist[lo] == 0) lo++
        var hi = 255
        while (hi > 0 && hist[hi] == 0) hi--

        val clipCount = (n * (clipPct / 100.0)).toLong().coerceAtLeast(0)
        var acc = 0L
        while (lo < 255 && acc + hist[lo] < clipCount) { acc += hist[lo]; lo++ }
        acc = 0L
        while (hi > 0 && acc + hist[hi] < clipCount) { acc += hist[hi]; hi-- }

        if (hi <= lo) {
            lo = 0; hi = 255
            for (p in pixels) { val v = p.coerceIn(0, 255); if (v < lo) lo = v; if (v > hi) hi = v }
        }
        if (hi <= lo) return IntArray(n) { pixels[it].coerceIn(0, 255) }

        val range = (hi - lo).toDouble()
        val invGamma = if (gamma > 0) 1.0 / gamma else 1.0
        val out = IntArray(n)
        for (i in pixels.indices) {
            val v = pixels[i].coerceIn(0, 255)
            val norm = ((v - lo) / range).coerceIn(0.0, 1.0)
            val g = Math.pow(norm, invGamma)
            out[i] = (g * 255.0).toInt().coerceIn(0, 255)
        }
        return out
    }

    fun grayscaleToBitmap(pixels: IntArray, w: Int, h: Int): Bitmap {
        val buf = IntArray(pixels.size)
        for (i in pixels.indices) {
            val v = pixels[i].coerceIn(0, 255)
            buf[i] = (0xFF shl 24) or (v shl 16) or (v shl 8) or v
        }
        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        bmp.setPixels(buf, 0, w, 0, 0, w, h)
        return bmp
    }

    fun invertedGrayscale(pixels: IntArray, w: Int, h: Int): IntArray {
        val out = IntArray(pixels.size)
        for (i in pixels.indices) out[i] = 255 - pixels[i].coerceIn(0, 255)
        return out
    }
}
