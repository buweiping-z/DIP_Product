package com.dip.material.ui.components

import android.Manifest
import android.content.pm.PackageManager
import android.util.Size
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.Camera
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat

@Composable
fun QrCodeScanner(
    onBarcodeScanned: (String) -> Unit,
    isActive: Boolean = true,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current

    var hasPermission by remember {
        mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
    }
    var showDenied by remember { mutableStateOf(false) }

    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasPermission = granted; if (!granted) showDenied = true
    }

    LaunchedEffect(Unit) { if (!hasPermission) launcher.launch(Manifest.permission.CAMERA) }

    if (showDenied) {
        AlertDialog(onDismissRequest = { showDenied = false },
            title = { Text("需要相机权限") },
            text = { Text("请在系统设置中授予相机权限以使用扫码功能") },
            confirmButton = { TextButton(onClick = { showDenied = false }) { Text("确定") } })
    }

    if (!hasPermission) {
        Box(modifier.background(Color.DarkGray), contentAlignment = Alignment.Center) {
            Text("需要相机权限", color = Color.White)
        }
        return
    }

    // 扫描状态：按钮控制开关
    val isScanning = remember { mutableStateOf(false) }

    // BarcodeAnalyzer：识别到条码后自动停止
    val analyzer = remember {
        var self: BarcodeAnalyzer? = null
        self = BarcodeAnalyzer { barcode ->
            isScanning.value = false
            onBarcodeScanned(barcode)
        }
        self!!
    }
    DisposableEffect(analyzer) { onDispose { analyzer.close() } }

    // isActive=true时自动启动扫描
    LaunchedEffect(isActive) {
        if (isActive) {
            isScanning.value = true
            analyzer.isActive.set(true)
        }
    }

    fun startScan() {
        isScanning.value = true
        analyzer.isActive.set(true)
    }

    // 相机曝光补偿
    val cameraRef = remember { mutableStateOf<Camera?>(null) }
    DisposableEffect(cameraRef.value) {
        val cam = cameraRef.value
        if (cam != null) {
            val range = cam.cameraInfo.exposureState.exposureCompensationRange
            val current = cam.cameraInfo.exposureState.exposureCompensationIndex
            val target = (current + 3).coerceIn(range.lower, range.upper)
            if (target != current) {
                cam.cameraControl.setExposureCompensationIndex(target)
            }
            onDispose {
                try { cam.cameraControl.setExposureCompensationIndex(current) }
                catch (_: Exception) {}
            }
        } else {
            onDispose { }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(Color(0xCC000000)),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // ── 顶部：状态提示 ──
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(0.12f),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = if (isScanning.value) "扫描中..." else "将条码对准取景框",
                color = Color.White.copy(alpha = 0.9f),
                fontSize = 14.sp,
                textAlign = TextAlign.Center
            )
        }

        // ── 中部：相机预览 + 取景框 ──
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(0.55f)
                .padding(horizontal = 24.dp)
                .clip(RoundedCornerShape(16.dp))
        ) {
            AndroidView(
                modifier = Modifier.fillMaxSize(),
                factory = { ctx ->
                    val previewView = PreviewView(ctx).apply {
                        scaleType = PreviewView.ScaleType.FILL_CENTER
                    }
                    val provider = ProcessCameraProvider.getInstance(ctx)
                    provider.addListener({
                        val cam = provider.get()
                        val preview = Preview.Builder().build().also {
                            it.surfaceProvider = previewView.surfaceProvider
                        }
                        val analysis = ImageAnalysis.Builder()
                            .setTargetResolution(Size(1280, 720))
                            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                            .build()
                        analysis.setAnalyzer(ContextCompat.getMainExecutor(ctx), analyzer)
                        try {
                            cam.unbindAll()
                            cameraRef.value = cam.bindToLifecycle(
                                lifecycleOwner,
                                CameraSelector.DEFAULT_BACK_CAMERA,
                                preview,
                                analysis
                            )
                        } catch (_: Exception) {}
                    }, ContextCompat.getMainExecutor(ctx))
                    previewView
                }
            )
            ScannerOverlay(
                modifier = Modifier.fillMaxSize(),
                isActive = isScanning.value
            )
        }

        // ── 底部：扫描按钮 ──
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .weight(0.33f),
            contentAlignment = Alignment.Center
        ) {
            Button(
                onClick = { startScan() },
                enabled = !isScanning.value,
                modifier = Modifier
                    .fillMaxWidth(0.65f)
                    .height(56.dp),
                shape = RoundedCornerShape(28.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = Color(0xFF4CAF50),
                    disabledContainerColor = Color(0xFF4CAF50).copy(alpha = 0.5f)
                )
            ) {
                Text(
                    text = if (isScanning.value) "⏳ 扫描中..." else "🔍 扫  码",
                    color = Color.White,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
            }
        }
    }
}
