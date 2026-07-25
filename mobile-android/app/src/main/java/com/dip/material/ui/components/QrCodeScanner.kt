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
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat

/**
 * 条码扫描组件 — 按钮触发模式。
 * 打开时不自动扫描，需点击底部"扫码"按钮才开始；
 * 识别到条码后自动停止，再次扫描需重新点击按钮。
 * 默认 2× 数码变焦，支持双指捏合缩放、点击复位。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun QrCodeScanner(
    onBarcodeScanned: (String) -> Unit,
    isActive: Boolean = false,
    scanMode: ScanMode = ScanMode.AUTO,
    params: PcbTuneParams = PcbTuneParams(),
    onClose: (() -> Unit)? = null,
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val cameraRef = remember { mutableStateOf<Camera?>(null) }
    val providerRef = remember { mutableStateOf<ProcessCameraProvider?>(null) }
    var zoomRatio by remember { mutableStateOf(2f) }
    var isScanning by remember { mutableStateOf(false) }

    var hasCameraPermission by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) ==
                    PackageManager.PERMISSION_GRANTED
        )
    }
    var showPermissionDeniedDialog by remember { mutableStateOf(false) }

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        hasCameraPermission = granted
        if (!granted) showPermissionDeniedDialog = true
    }

    LaunchedEffect(Unit) {
        if (!hasCameraPermission) permissionLauncher.launch(Manifest.permission.CAMERA)
    }

    if (showPermissionDeniedDialog) {
        AlertDialog(
            onDismissRequest = { showPermissionDeniedDialog = false },
            title = { Text("需要相机权限") },
            text = { Text("请在系统设置中授予相机权限以使用扫码功能") },
            confirmButton = {
                TextButton(onClick = { showPermissionDeniedDialog = false }) { Text("确定") }
            }
        )
    }

    if (!hasCameraPermission) {
        Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Text("需要相机权限才能扫码，请授予权限后重试")
        }
        return
    }

    // 中间状态：analyzer 回调写入，LaunchedEffect 读取并处理停止逻辑
    var scanResult by remember { mutableStateOf<String?>(null) }

    val analyzer = remember {
        BarcodeAnalyzer(onBarcodeScanned = { code ->
            scanResult = code
        }, scanMode, params)
    }

    // 拿到扫描结果 → 停扫 + 回调
    LaunchedEffect(scanResult) {
        val code = scanResult ?: return@LaunchedEffect
        isScanning = false
        analyzer.isActive.set(false)
        onBarcodeScanned(code)
        scanResult = null
    }

    LaunchedEffect(scanMode) { analyzer.scanMode = scanMode }

    // 启动扫描：激活分析器
    fun startScan() {
        isScanning = true
        analyzer.isActive.set(true)
    }

    LaunchedEffect(isActive) {
        if (isActive) startScan()
    }

    // 离开组合时强制释放相机
    DisposableEffect(Unit) {
        onDispose {
            analyzer.close()
            try { providerRef.value?.unbindAll() } catch (_: Exception) {}
        }
    }

    Column(modifier = modifier.fillMaxSize()) {
        // 相机预览区域（占据剩余空间）
        Box(modifier = Modifier.weight(1f).fillMaxWidth().pointerInput(Unit) {
            detectTransformGestures { _, _, zoom, _ ->
                val cam = cameraRef.value ?: return@detectTransformGestures
                val zs = cam.cameraInfo.zoomState.value ?: return@detectTransformGestures
                val next = (zoomRatio * zoom).coerceIn(zs.minZoomRatio, zs.maxZoomRatio)
                zoomRatio = next
                cam.cameraControl.setZoomRatio(next)
            }
        }) {
            AndroidView(
                modifier = Modifier.fillMaxSize(),
                factory = { ctx ->
                    val previewView = PreviewView(ctx)
                    val cameraProviderFuture = ProcessCameraProvider.getInstance(ctx)

                    cameraProviderFuture.addListener({
                        val cameraProvider = cameraProviderFuture.get()
                        providerRef.value = cameraProvider
                        val preview = Preview.Builder().build().also {
                            it.setSurfaceProvider(previewView.surfaceProvider)
                        }
                        val imageAnalysis = ImageAnalysis.Builder()
                            .setTargetResolution(Size(1920, 1080))
                            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                            .build()
                        imageAnalysis.setAnalyzer(ContextCompat.getMainExecutor(ctx), analyzer)

                        try {
                            cameraProvider.unbindAll()
                            val camera = cameraProvider.bindToLifecycle(
                                lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA,
                                preview, imageAnalysis
                            )
                            cameraRef.value = camera
                            val zs = camera.cameraInfo.zoomState.value
                            if (zs != null) {
                                val initZoom = 2f.coerceIn(zs.minZoomRatio, zs.maxZoomRatio)
                                zoomRatio = initZoom
                                camera.cameraControl.setZoomRatio(initZoom)
                            }
                        } catch (_: Exception) { }
                    }, ContextCompat.getMainExecutor(ctx))
                    previewView
                }
            )

            ScannerOverlay(modifier = Modifier.fillMaxSize(), isActive = isScanning)

            // 变焦指示 + 点击复位
            Surface(
                modifier = Modifier
                    .align(Alignment.TopStart)
                    .padding(top = 12.dp, start = 12.dp)
                    .clickable {
                        cameraRef.value?.cameraControl?.setZoomRatio(1f)
                        zoomRatio = 1f
                    },
                tonalElevation = 4.dp,
                shape = MaterialTheme.shapes.small
            ) {
                Text(
                    text = "变焦 ${"%.1f".format(zoomRatio)}×",
                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp),
                    style = MaterialTheme.typography.labelSmall
                )
            }

            // 关闭按钮
            if (onClose != null) {
                IconButton(
                    onClick = onClose,
                    modifier = Modifier.align(Alignment.TopEnd).padding(top = 8.dp, end = 8.dp)
                ) {
                    Icon(Icons.Filled.Close, "关闭", tint = Color.White)
                }
            }
        }

        // 底部扫码按钮
        Surface(
            modifier = Modifier.fillMaxWidth(),
            shadowElevation = 8.dp,
            color = MaterialTheme.colorScheme.surface
        ) {
            Button(
                onClick = { startScan() },
                enabled = !isScanning,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp, vertical = 16.dp)
                    .height(56.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = Color(0xFF4CAF50),
                    disabledContainerColor = Color(0xFFBDBDBD)
                )
            ) {
                Text(
                    text = if (isScanning) "正在扫描..." else "扫码",
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color.White
                )
            }
        }
    }
}
