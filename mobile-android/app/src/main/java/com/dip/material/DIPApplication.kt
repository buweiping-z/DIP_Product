package com.dip.material

import android.app.Application
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import com.dip.material.data.network.RetrofitClient
import com.dip.material.data.network.TokenHolder
import com.dip.material.utils.ScanSoundManager

class DIPApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        ScanSoundManager.init(this)
        TokenHolder.init(this)
        registerWifiCallback()
    }

    /**
     * 监听 WiFi 网络状态变化，WiFi 恢复时自动重建 Retrofit 客户端。
     *
     * 背景：RetrofitClient 在首次创建 OkHttp 时绑定 WiFi Network（WifiSocketFactory），
     * 防止手机同时开 WiFi+移动数据时请求走错通道。但 WiFi 断开后旧 Network 失效，
     * bindSocket 报 ENONET。必须等到 apiService 重建（重新获取新 Network）才能恢复。
     * 这里监听 WiFi 可用性变化，WiFi 恢复时主动 reset，下次请求自动绑定新 Network。
     */
    private fun registerWifiCallback() {
        val cm = getSystemService(ConnectivityManager::class.java) ?: return
        val request = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .build()
        cm.registerNetworkCallback(request, object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                // WiFi 恢复 → 清空缓存的 apiService，下次请求重建 OkHttp + 绑定新 Network
                RetrofitClient.reset()
            }
            // onLost 不 reset：保留旧 apiService 让后续请求立即报 ENONET（立刻知道断网），
            // 而非清空后走默认 SocketFactory → 移动数据通道 → 15 秒超时才报错
        })
    }
}
