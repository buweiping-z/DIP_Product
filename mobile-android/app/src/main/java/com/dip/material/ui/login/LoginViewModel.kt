package com.dip.material.ui.login

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.dip.material.data.repository.AppRepository
import com.dip.material.data.network.RetrofitClient
import com.dip.material.utils.PreferencesManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class LoginUiState(
    val username: String = "", val password: String = "",
    val savedUsername: String = "",
    val isLoading: Boolean = false, val error: String? = null,
    val isLoggedIn: Boolean = false, val serverUrl: String = ""
)

class LoginViewModel(application: Application) : AndroidViewModel(application) {
    private val repo = AppRepository(application)
    private val prefs = PreferencesManager(application)
    private val _state = MutableStateFlow(LoginUiState())
    val state: StateFlow<LoginUiState> = _state.asStateFlow()

    fun loadServerUrl() {
        viewModelScope.launch {
            val url = prefs.serverUrl.first()
            if (url.isNotBlank()) {
                RetrofitClient.baseUrl = url
                RetrofitClient.reset()
            }
            _state.update { it.copy(serverUrl = url) }
        }
    }

    fun loadCredentials() {
        viewModelScope.launch {
            val saved = prefs.token.first()
            if (saved.isNotBlank()) {
                // Token exists, verify it
                repo.getCurrentUser().fold(
                    onSuccess = { if (it.data != null) _state.update { s -> s.copy(isLoggedIn = true) } },
                    onFailure = {}
                )
            }
        }
    }

    fun login(username: String, password: String) {
        viewModelScope.launch {
            _state.update { it.copy(isLoading = true, error = null) }
            repo.login(username, password).fold(
                onSuccess = { res ->
                    if (res.code == 0 && res.data != null) {
                        val d = res.data
                        prefs.saveTokens(d.accessToken, d.refreshToken)
                        prefs.saveUsername(username)
                        _state.update { it.copy(isLoggedIn = true, isLoading = false) }
                    } else {
                        _state.update { it.copy(isLoading = false, error = res.message ?: "登录失败") }
                    }
                },
                onFailure = { e -> _state.update { it.copy(isLoading = false, error = e.message) } }
            )
        }
    }

    fun saveServerUrl(url: String) {
        viewModelScope.launch {
            prefs.saveServerUrl(url)
            RetrofitClient.baseUrl = url
            RetrofitClient.reset()
            _state.update { it.copy(serverUrl = url) }
        }
    }
}
