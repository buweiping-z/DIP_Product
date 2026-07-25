package com.dip.material.ui.components

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.dp

/**
 * 扫码取景框覆盖层（移植自 machine_check）
 * - 四角边框 + 中间透明、四周半透明遮罩
 * - 动态扫描线上下移动
 */
@Composable
fun ScannerOverlay(modifier: Modifier = Modifier, isActive: Boolean = true) {
    val scanLineProgress = rememberInfiniteTransition()
    val scanYOffset by scanLineProgress.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(1500, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        )
    )

    val cornerLength = 40.dp
    val strokeWidth = 3.dp
    val frameWidthFraction = 0.95f
    val frameHeightFraction = 0.45f

    Box(modifier = modifier) {
        Canvas(modifier = Modifier.fillMaxSize()) {
            val canvasW = size.width
            val canvasH = size.height

            val frameW = canvasW * frameWidthFraction
            val frameH = canvasW * frameHeightFraction
            val frameX = (canvasW - frameW) / 2f
            val frameY = (canvasH - frameH) / 2f

            // 遮罩（取景框外压暗）
            val scrimColor = Color.Black.copy(alpha = 0.45f)
            drawRect(scrimColor, topLeft = Offset(0f, 0f), size = Size(canvasW, frameY))
            drawRect(scrimColor, topLeft = Offset(0f, frameY + frameH), size = Size(canvasW, canvasH - (frameY + frameH)))
            drawRect(scrimColor, topLeft = Offset(0f, frameY), size = Size(frameX, frameH))
            drawRect(scrimColor, topLeft = Offset(frameX + frameW, frameY), size = Size(canvasW - (frameX + frameW), frameH))

            // 四角边框
            val cornerColor = Color(0xFF4CAF50)
            drawLine(cornerColor, Offset(frameX, frameY + cornerLength.toPx()), Offset(frameX, frameY), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX, frameY), Offset(frameX + cornerLength.toPx(), frameY), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX + frameW, frameY + cornerLength.toPx()), Offset(frameX + frameW, frameY), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX + frameW, frameY), Offset(frameX + frameW - cornerLength.toPx(), frameY), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX, frameY + frameH - cornerLength.toPx()), Offset(frameX, frameY + frameH), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX, frameY + frameH), Offset(frameX + cornerLength.toPx(), frameY + frameH), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX + frameW, frameY + frameH - cornerLength.toPx()), Offset(frameX + frameW, frameY + frameH), strokeWidth.toPx())
            drawLine(cornerColor, Offset(frameX + frameW, frameY + frameH), Offset(frameX + frameW - cornerLength.toPx(), frameY + frameH), strokeWidth.toPx())

            // 扫描线
            if (isActive) {
                val lineY = frameY + (frameH * scanYOffset)
                drawLine(
                    color = Color(0xFF4CAF50).copy(alpha = 0.8f),
                    start = Offset(frameX + 10.dp.toPx(), lineY),
                    end = Offset(frameX + frameW - 10.dp.toPx(), lineY),
                    strokeWidth = 2.dp.toPx()
                )
                drawLine(
                    color = Color(0xFF4CAF50).copy(alpha = 0.3f),
                    start = Offset(frameX + 5.dp.toPx(), lineY),
                    end = Offset(frameX + frameW - 5.dp.toPx(), lineY + 20.dp.toPx()),
                    strokeWidth = 10.dp.toPx()
                )
            }
        }

        Text(
            text = "将条码对准取景框",
            modifier = Modifier.align(Alignment.BottomCenter).padding(24.dp),
            color = Color.White.copy(alpha = 0.9f),
            style = TextStyle(fontSize = 14.sp)
        )
    }
}
