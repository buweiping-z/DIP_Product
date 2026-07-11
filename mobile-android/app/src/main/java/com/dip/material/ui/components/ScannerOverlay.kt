package com.dip.material.ui.components

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.sp
import androidx.compose.ui.unit.dp

/**
 * 扫码取景框覆盖层
 * - 四角绿色边框
 * - 取景框内部完全透明（预览清晰可见）
 * - 取景框外围半透明遮罩
 * - 动态扫描线
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
    val frameSizeFraction = 0.45f // 取景框占屏幕宽度的比例

    Box(modifier = modifier) {
        Canvas(modifier = Modifier.fillMaxSize()) {
            val canvasW = size.width
            val canvasH = size.height

            // 取景框尺寸
            val frameW = canvasW * frameSizeFraction
            val frameH = frameW
            val frameX = (canvasW - frameW) / 2f
            val frameY = (canvasH - frameH) / 2f

            // 1. 绘制四周遮罩（EvenOdd 填充：取景框内部挖洞透明）
            val maskPath = Path().apply {
                addRect(Rect(0f, 0f, canvasW, canvasH))
                addRect(Rect(frameX, frameY, frameX + frameW, frameY + frameH))
                fillType = PathFillType.EvenOdd
            }
            drawPath(maskPath, color = Color.Black.copy(alpha = 0.55f))

            // 2. 取景框白色细边框
            drawRect(
                color = Color.White.copy(alpha = 0.35f),
                topLeft = Offset(frameX, frameY),
                size = Size(frameW, frameH),
                style = Stroke(width = 1.dp.toPx())
            )

            // 3. 四角绿色加粗边框
            val cornerColor = Color(0xFF4CAF50)
            val cornerPxW = cornerLength.toPx()
            val sw = strokeWidth.toPx()

            // 左上角
            drawLine(cornerColor, Offset(frameX, frameY + cornerPxW), Offset(frameX, frameY), sw)
            drawLine(cornerColor, Offset(frameX, frameY), Offset(frameX + cornerPxW, frameY), sw)
            // 右上角
            drawLine(cornerColor, Offset(frameX + frameW - cornerPxW, frameY), Offset(frameX + frameW, frameY), sw)
            drawLine(cornerColor, Offset(frameX + frameW, frameY), Offset(frameX + frameW, frameY + cornerPxW), sw)
            // 左下角
            drawLine(cornerColor, Offset(frameX, frameY + frameH - cornerPxW), Offset(frameX, frameY + frameH), sw)
            drawLine(cornerColor, Offset(frameX, frameY + frameH), Offset(frameX + cornerPxW, frameY + frameH), sw)
            // 右下角
            drawLine(cornerColor, Offset(frameX + frameW - cornerPxW, frameY + frameH), Offset(frameX + frameW, frameY + frameH), sw)
            drawLine(cornerColor, Offset(frameX + frameW, frameY + frameH - cornerPxW), Offset(frameX + frameW, frameY + frameH), sw)

            // 4. 扫描线（取景框内部）
            if (isActive) {
                val lineY = frameY + (frameH * scanYOffset)
                drawLine(
                    color = Color(0xFF4CAF50).copy(alpha = 0.8f),
                    start = Offset(frameX + 10.dp.toPx(), lineY),
                    end = Offset(frameX + frameW - 10.dp.toPx(), lineY),
                    strokeWidth = 2.dp.toPx()
                )
                // 扫描线渐变光晕
                drawLine(
                    color = Color(0xFF4CAF50).copy(alpha = 0.25f),
                    start = Offset(frameX + 5.dp.toPx(), lineY),
                    end = Offset(frameX + frameW - 5.dp.toPx(), lineY + 20.dp.toPx()),
                    strokeWidth = 12.dp.toPx()
                )
            }
        }

        // 底部提示文字
        Text(
            text = "将条码对准取景框",
            modifier = Modifier.align(Alignment.BottomCenter).padding(24.dp),
            color = Color.White.copy(alpha = 0.9f),
            style = TextStyle(fontSize = 14.sp)
        )
    }
}
