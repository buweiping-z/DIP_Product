package com.dip.material.data.network

import android.content.Context
import com.dip.material.utils.PreferencesManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

/**
 * Token 内存缓存 —— 替代每次从 DataStore Flow 读取，消除 runBlocking 阻塞 OkHttp 线程。
 *
 * 写入路径（同时写内存 + 持久化 DataStore）：
 *   - 登录成功 → save()
 *   - Token 自动刷新 → save()
 * 读取路径（纯内存，零阻塞）：
 *   - AuthInterceptor → accessToken
 *   - TokenAuthenticator → refreshToken
 * 清除路径：退出登录 → clear()
 *
 * 初始化在 [DIPApplication.onCreate] 中调用 [init] 从 DataStore 加载缓存的 Token。
 */
object TokenHolder {
    @Volatile var accessToken: String = ""
    @Volatile var refreshToken: String = ""

    private var prefs: PreferencesManager? = null

    /** 启动时从 DataStore 加载持久化的 Token */
    fun init(context: Context) {
        prefs = PreferencesManager(context.applicationContext)
        accessToken = runBlocking { prefs!!.token.first() }
        refreshToken = runBlocking { prefs!!.refreshToken.first() }
    }

    /** 登录成功 / Token 刷新后调用 —— 同时写内存 + 异步持久化 */
    fun save(access: String, refresh: String) {
        accessToken = access
        refreshToken = refresh
        val p = prefs ?: return
        CoroutineScope(Dispatchers.IO).launch { p.saveTokens(access, refresh) }
    }

    /** 退出登录调用 —— 同时清内存 + 异步清除持久化 */
    fun clear() {
        accessToken = ""
        refreshToken = ""
        val p = prefs ?: return
        CoroutineScope(Dispatchers.IO).launch { p.clearTokens() }
    }
}
