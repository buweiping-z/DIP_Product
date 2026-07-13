package com.dip.material.data.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.net.InetAddress
import java.net.Socket
import java.util.concurrent.TimeUnit
import javax.net.SocketFactory

object RetrofitClient {
    var baseUrl: String = "http://192.168.5.11:8800/"
    private var apiService: ApiService? = null

    fun getApiService(context: Context): ApiService {
        if (apiService == null) {
            val logging = HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BODY }

            val clientBuilder = OkHttpClient.Builder()
                .addInterceptor(logging)
                .addInterceptor(AuthInterceptor(context))
                .connectTimeout(15, TimeUnit.SECONDS)
                .readTimeout(15, TimeUnit.SECONDS)
                .writeTimeout(15, TimeUnit.SECONDS)

            // 绑定 WiFi 网卡（手机同时开 WiFi+移动数据时，强制走 WiFi）
            val wifiNetwork = getWifiNetwork(context)
            if (wifiNetwork != null) {
                clientBuilder.socketFactory(WifiSocketFactory(wifiNetwork))
            }

            val client = clientBuilder.build()
            apiService = Retrofit.Builder()
                .baseUrl(baseUrl)
                .client(client)
                .addConverterFactory(GsonConverterFactory.create())
                .build()
                .create(ApiService::class.java)
        }
        return apiService!!
    }

    private fun getWifiNetwork(context: Context): Network? {
        val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager ?: return null
        for (net in cm.allNetworks) {
            val caps = cm.getNetworkCapabilities(net) ?: continue
            if (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
                return net
            }
        }
        return null
    }

    fun reset() { apiService = null }
}

/** SocketFactory 装饰器：强制 Socket 绑定到指定的 WiFi Network */
private class WifiSocketFactory(
    private val network: Network,
    private val delegate: SocketFactory = SocketFactory.getDefault()
) : SocketFactory() {
    override fun createSocket(): Socket {
        val socket = delegate.createSocket()
        network.bindSocket(socket)
        return socket
    }

    override fun createSocket(host: String, port: Int): Socket {
        val socket = delegate.createSocket()
        network.bindSocket(socket)
        socket.connect(java.net.InetSocketAddress(host, port))
        return socket
    }

    override fun createSocket(host: String, port: Int, localHost: InetAddress, localPort: Int): Socket {
        val socket = delegate.createSocket()
        network.bindSocket(socket)
        socket.connect(java.net.InetSocketAddress(host, port))
        return socket
    }

    override fun createSocket(host: InetAddress, port: Int): Socket {
        val socket = delegate.createSocket()
        network.bindSocket(socket)
        socket.connect(java.net.InetSocketAddress(host, port))
        return socket
    }

    override fun createSocket(host: InetAddress, port: Int, localHost: InetAddress, localPort: Int): Socket {
        val socket = delegate.createSocket()
        network.bindSocket(socket)
        socket.connect(java.net.InetSocketAddress(host, port))
        return socket
    }
}
