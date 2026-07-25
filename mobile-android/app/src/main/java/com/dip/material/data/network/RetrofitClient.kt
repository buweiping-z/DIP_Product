package com.dip.material.data.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import okhttp3.Authenticator
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.Route
import okhttp3.logging.HttpLoggingInterceptor
import org.json.JSONObject
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.net.InetAddress
import java.net.Socket
import java.util.concurrent.TimeUnit
import javax.net.SocketFactory

object RetrofitClient {
    var baseUrl: String = "http://192.168.5.11:8800/"
    private var apiService: ApiService? = null
    private var cachedNetworkHandle: Long = -1L
    private var currentWifiNetwork: Network? = null

    fun getApiService(context: Context): ApiService {
        // 兜底检测：WiFi Network handle 变了 → 旧 WifiSocketFactory 中的 Network 已失效 → 重建
        val currentHandle = getWifiNetwork(context)?.networkHandle ?: -1L
        if (apiService != null && currentHandle != cachedNetworkHandle) {
            apiService = null
        }

        if (apiService == null) {
            val logging = HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BASIC }

            currentWifiNetwork = getWifiNetwork(context)
            val clientBuilder = OkHttpClient.Builder()
                .addInterceptor(logging)
                .addInterceptor(AuthInterceptor())
                .authenticator(TokenAuthenticator())
                .connectTimeout(15, TimeUnit.SECONDS)
                .readTimeout(15, TimeUnit.SECONDS)
                .writeTimeout(15, TimeUnit.SECONDS)

            if (currentWifiNetwork != null) {
                clientBuilder.socketFactory(WifiSocketFactory(currentWifiNetwork!!))
            }

            val client = clientBuilder.build()
            apiService = Retrofit.Builder()
                .baseUrl(baseUrl)
                .client(client)
                .addConverterFactory(GsonConverterFactory.create())
                .build()
                .create(ApiService::class.java)
            cachedNetworkHandle = currentHandle
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

    fun reset() { apiService = null; cachedNetworkHandle = -1L; currentWifiNetwork = null }

    /** OkHttp Authenticator：token 过期（401）时自动用 refresh_token 换新 token 并重试 */
    private class TokenAuthenticator : Authenticator {
        override fun authenticate(route: Route?, response: Response): Request? {
            if (responseCount(response) > 1) return null

            val rt = TokenHolder.refreshToken
            if (rt.isBlank()) return null

            return try {
                val json = JSONObject().apply { put("refresh_token", rt) }
                val body = json.toString().toRequestBody("application/json".toMediaType())
                val refreshRequest = Request.Builder()
                    .url("${RetrofitClient.baseUrl}api/v1/auth/refresh")
                    .post(body)
                    .build()

                // 刷新请求复用 WiFi 绑定的 OkHttp（双网场景也能访问内网服务器）
                val refreshClient = OkHttpClient.Builder().apply {
                    val wifi = RetrofitClient.currentWifiNetwork
                    if (wifi != null) socketFactory(WifiSocketFactory(wifi))
                }.build()
                val refreshResponse = refreshClient.newCall(refreshRequest).execute()
                if (!refreshResponse.isSuccessful) {
                    TokenHolder.clear()
                    return null
                }
                val respJson = JSONObject(refreshResponse.body?.string() ?: "")
                val data = respJson.optJSONObject("data") ?: return null
                val newToken = data.optString("access_token", "")
                val newRt = data.optString("refresh_token", "")
                if (newToken.isBlank()) return null

                TokenHolder.save(newToken, newRt)
                response.request.newBuilder()
                    .header("Authorization", "Bearer $newToken")
                    .build()
            } catch (_: Exception) {
                null
            }
        }

        private fun responseCount(response: Response): Int {
            var count = 0
            var r: Response? = response
            while (r != null) { count++; r = r.priorResponse }
            return count
        }
    }
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
